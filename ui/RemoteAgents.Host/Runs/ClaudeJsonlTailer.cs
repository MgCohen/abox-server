using System.Text.Json;
using RemoteAgents.Agents;
using RemoteAgents.Chat;
using RemoteAgents.Host.Sinks;

namespace RemoteAgents.Host.Runs;

// Live-tails ~/.claude/projects/<encoded-cwd>/<session-id>.jsonl that
// Claude writes for every run (interactive TUI or headless). Each new
// line is a typed JSON object — user/assistant message, tool use, tool
// result, thinking content, summary. We parse the meaningful blocks and
// emit structured ChatEvents into the run's ChatChannel.
//
// Strategy: poll-and-reread. On each tick, snapshot the file as a list of
// lines via File.ReadAllLines, skip the lines we've already emitted, and
// process the new tail. This avoids the Windows-FileStream-caches-EOF
// issue that breaks the keep-open-and-ReadLineAsync approach — the
// kernel always reports the current on-disk length when we open the
// handle fresh each poll. The trade-off is O(N) per poll where N is
// Claude's line count for the session; in practice ~tens to hundreds,
// which is fine at a 150ms interval.
//
// Reference implementations using the same pattern: claude-code-trace,
// tail-claude, clog (see C7 research findings).
public sealed class ClaudeJsonlTailer
{
    private readonly ILogger _log;
    private readonly Run _run;
    private int _emittedLineCount;
    private int _linesRead;
    private int _eventsEmitted;

    public ClaudeJsonlTailer(Run run, ILogger log)
    {
        _run = run;
        _log = log;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        if (_run.ProjectDir is null) return;

        var dirPath = Path.GetDirectoryName(ClaudeJsonl.PathFor(_run.ProjectDir, "x"));
        if (dirPath is null) return;

        Directory.CreateDirectory(dirPath);
        var preExisting = new HashSet<string>(
            Directory.EnumerateFiles(dirPath, "*.jsonl").Select(Path.GetFileName)!,
            StringComparer.OrdinalIgnoreCase);

        // Wait up to 30s for Claude to create a NEW .jsonl that wasn't
        // there at run start. Polling is fine — Claude usually creates
        // its session file within a couple seconds of TUI launch.
        string? path = null;
        for (var i = 0; i < 300 && path is null && !ct.IsCancellationRequested; i++)
        {
            await Task.Delay(100, ct);
            try
            {
                foreach (var f in Directory.EnumerateFiles(dirPath, "*.jsonl"))
                {
                    var name = Path.GetFileName(f);
                    if (preExisting.Contains(name)) continue;
                    _run.ClaudeSessionId = Path.GetFileNameWithoutExtension(name);
                    path = f;
                    break;
                }
            }
            catch (IOException) { /* dir transient */ }
        }
        if (path is null)
        {
            _log.LogWarning("No new Claude JSONL appeared under {Dir} within 30s for run {RunId}", dirPath, _run.Id);
            return;
        }

        _log.LogInformation("Run {RunId} tailing Claude JSONL: {Path}", _run.Id, path);

        // Poll loop — re-read the file each tick. When EndedAt is set,
        // do one final pass after a brief grace window to catch the
        // final-flush writes from Claude, then exit.
        var endedAtSnapshot = (DateTimeOffset?)null;
        while (!ct.IsCancellationRequested)
        {
            await ProcessFileAsync(path, ct);

            if (_run.EndedAt is not null)
            {
                if (endedAtSnapshot is null)
                {
                    endedAtSnapshot = _run.EndedAt;
                    // grace window for Claude's last flush after PTY exit
                    await Task.Delay(400, ct);
                    continue;
                }
                // One full poll after grace already happened; exit.
                _log.LogInformation("Run {RunId} chat tailer done — read={Read} emitted={Emitted}",
                    _run.Id, _linesRead, _eventsEmitted);
                return;
            }

            await Task.Delay(150, ct);
        }
    }

    private async Task ProcessFileAsync(string path, CancellationToken ct)
    {
        string[] lines;
        try
        {
            // File.ReadAllLinesAsync opens + reads + closes — sees the
            // current on-disk file length every call.
            lines = await File.ReadAllLinesAsync(path, ct);
        }
        catch (IOException)
        {
            // Transient sharing violation; try again next tick.
            return;
        }

        for (var i = _emittedLineCount; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) { _emittedLineCount++; continue; }

            // Partial-line resilience: if a line doesn't end with `}` (the
            // closing brace of the JSON object) it might be mid-write.
            // Don't advance the counter — re-try on next poll.
            var trimmed = line.TrimEnd();
            if (trimmed.Length == 0 || trimmed[^1] != '}')
                return;

            _emittedLineCount++;
            _linesRead++;
            await EmitLineAsync(line, ct);
        }
    }

    private async Task EmitLineAsync(string line, CancellationToken ct)
    {
        if (line.Length == 0 || line[0] != '{') return;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch (JsonException ex)
        {
            _log.LogDebug(ex, "Bad JSONL line for run {RunId}", _run.Id);
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;

            var uuid = root.TryGetProperty("uuid", out var u) && u.ValueKind == JsonValueKind.String
                ? u.GetString() ?? "" : "";
            var at = root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String
                ? DateTimeOffset.TryParse(ts.GetString(), out var parsed) ? parsed : DateTimeOffset.UtcNow
                : DateTimeOffset.UtcNow;

            var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() ?? "" : "";

            if (type == "summary")
            {
                var summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
                await TryEmit(new ChatEvent.Meta(at, uuid, "summary", summary), ct);
                return;
            }

            if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object)
                return;

            if (msg.TryGetProperty("role", out var r) && r.ValueKind == JsonValueKind.String)
                type = r.GetString() ?? type;

            if (type == "user" && root.TryGetProperty("isMeta", out var isMeta)
                && isMeta.ValueKind == JsonValueKind.True)
                return;

            if (!msg.TryGetProperty("content", out var content)) return;

            // Skip user messages that are local-command synthesised text
            // (e.g. /exit, /resume) — they're TUI plumbing, not real
            // conversation content the user wrote.
            if (type == "user" && content.ValueKind == JsonValueKind.String)
            {
                var raw = content.GetString() ?? "";
                if (IsLocalCommandWrapper(raw)) return;
            }

            if (content.ValueKind == JsonValueKind.String)
            {
                var txt = content.GetString() ?? "";
                if (txt.Length == 0) return;
                if (type == "user")      await TryEmit(new ChatEvent.UserText(at, uuid, txt), ct);
                else if (type == "assistant") await TryEmit(new ChatEvent.AssistantText(at, uuid, txt), ct);
                return;
            }

            if (content.ValueKind != JsonValueKind.Array) return;

            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object) continue;
                var bt = block.TryGetProperty("type", out var btv) && btv.ValueKind == JsonValueKind.String
                    ? btv.GetString() ?? "" : "";

                switch (bt)
                {
                    case "text":
                        var text = block.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";
                        if (text.Length == 0) break;
                        if (type == "assistant") await TryEmit(new ChatEvent.AssistantText(at, uuid, text), ct);
                        else if (type == "user") await TryEmit(new ChatEvent.UserText(at, uuid, text), ct);
                        break;

                    case "thinking":
                        var think = block.TryGetProperty("thinking", out var th) ? th.GetString() ?? "" : "";
                        if (think.Length > 0)
                            await TryEmit(new ChatEvent.Thinking(at, uuid, think), ct);
                        break;

                    case "tool_use":
                        var toolId = block.TryGetProperty("id", out var ti) ? ti.GetString() ?? "" : "";
                        var name = block.TryGetProperty("name", out var tn) ? tn.GetString() ?? "" : "";
                        var inputJson = block.TryGetProperty("input", out var ip) ? ip.GetRawText() : "{}";
                        await TryEmit(new ChatEvent.ToolUse(at, uuid, toolId, name, inputJson), ct);
                        break;

                    case "tool_result":
                        var useId = block.TryGetProperty("tool_use_id", out var ui) ? ui.GetString() ?? "" : "";
                        var isErr = block.TryGetProperty("is_error", out var ie) && ie.ValueKind == JsonValueKind.True;
                        var rcontent = ExtractToolResultContent(block);
                        await TryEmit(new ChatEvent.ToolResult(at, uuid, useId, isErr, rcontent), ct);
                        break;

                    default:
                        await TryEmit(new ChatEvent.Meta(at, uuid, bt, ""), ct);
                        break;
                }
            }
        }
    }

    private static bool IsLocalCommandWrapper(string text)
    {
        var t = text.TrimStart();
        return t.StartsWith("<command-name>")
            || t.StartsWith("<command-message>")
            || t.StartsWith("<command-args>")
            || t.StartsWith("<local-command-stdout>")
            || t.StartsWith("<local-command-stderr>")
            || t.StartsWith("<local-command-caveat>");
    }

    private static string ExtractToolResultContent(JsonElement block)
    {
        if (!block.TryGetProperty("content", out var c)) return "";
        if (c.ValueKind == JsonValueKind.String) return c.GetString() ?? "";
        if (c.ValueKind == JsonValueKind.Array)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var item in c.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (item.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(t.GetString());
                }
            }
            return sb.ToString();
        }
        return c.GetRawText();
    }

    private async Task TryEmit(ChatEvent evt, CancellationToken ct)
    {
        try
        {
            await _run.Chat.EmitAsync(evt, ct);
            _eventsEmitted++;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.LogWarning(ex, "Failed to emit chat event for run {RunId}", _run.Id); }
    }
}
