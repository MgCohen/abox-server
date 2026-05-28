using System.Diagnostics;
using System.Text;
using System.Text.Json;
using RemoteAgents.Events;

namespace RemoteAgents.Agents;

// Drives `codex exec` non-interactively. codex exec is officially supported
// on ChatGPT subscriptions (since April 2026), so no PTY trick needed — a
// plain Process is fine. Captures the agent's final message via `-o
// <tmpfile>` and scans --json stdout for the session id.
//
// Non-sealed (Q7) for the same reason as ClaudeAgent — concrete projects
// may want to swap in a different model or different sandbox policy.
public class CodexAgent : Agent
{
    public CodexAgentOptions Options { get; init; } = new();

    public static List<string> BuildCodexArgs(string? sessionId, string projectDir, string lastMessageFile, CodexAgentOptions opts)
    {
        var args = new List<string>();
        if (sessionId is not null) { args.Add("exec"); args.Add("resume"); args.Add(sessionId); }
        else                        { args.Add("exec"); }

        args.Add("--cd"); args.Add(projectDir);
        args.Add("-o"); args.Add(lastMessageFile);
        args.Add("--sandbox"); args.Add(opts.Sandbox);
        args.Add("--dangerously-bypass-approvals-and-sandbox");
        args.Add("--json");
        if (!string.IsNullOrEmpty(opts.Model)) { args.Add("--model"); args.Add(opts.Model); }

        // `-` tells codex to read the prompt from stdin.
        args.Add("-");
        return args;
    }

    protected override async Task<AgentResult> ExecuteAsync(AgentRunRequest req, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.Prompt)) throw new ArgumentException("prompt is required", nameof(req));
        if (string.IsNullOrEmpty(req.ProjectDir)) throw new ArgumentException("projectDir is required", nameof(req));

        var tmpDir = Path.Combine(Path.GetTempPath(), "agents-codex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var lastMessageFile = Path.Combine(tmpDir, "last.txt");

        var args = BuildCodexArgs(req.SessionId, req.ProjectDir, lastMessageFile, Options);
        var commandLine = "codex " + string.Join(' ', args.Select(QuoteIfNeeded));

        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = $"/c {commandLine}",
            WorkingDirectory = req.ProjectDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // Defense in depth: blank OPENAI_API_KEY in the child env so codex
        // can't accidentally fall through to API billing.
        psi.Environment["OPENAI_API_KEY"] = "";

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        string? extractedSessionId = req.SessionId;

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdout.AppendLine(e.Data);
            if (extractedSessionId is null)
            {
                var found = ScanForSessionId(e.Data);
                if (found is not null) extractedSessionId = found;
            }
            // Mirror the chunk into the sink for live observability. Fire-and-forget
            // on Task — the lifecycle's ct will tear down the proc on shutdown.
            _ = Sink.EmitAsync(new AgentEvent.StreamChunk(DateTimeOffset.UtcNow, Name, e.Data + "\n"));
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) stderr.AppendLine(e.Data);
        };

        // System prompt prepended to user prompt — codex has no flag for it.
        var fullPrompt = string.IsNullOrEmpty(Options.SystemPrompt)
            ? req.Prompt
            : Options.SystemPrompt + "\n\n" + req.Prompt;

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.StandardInput.WriteAsync(fullPrompt);
        proc.StandardInput.Close();

        using var timeoutCts = new CancellationTokenSource(Options.JsonStreamTimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try { await proc.WaitForExitAsync(linkedCts.Token); }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            try { await proc.WaitForExitAsync(CancellationToken.None); } catch { }
            if (!timeoutCts.IsCancellationRequested) throw;
        }

        string text = "";
        if (File.Exists(lastMessageFile))
            text = await File.ReadAllTextAsync(lastMessageFile, ct);

        try { Directory.Delete(tmpDir, recursive: true); } catch { /* best-effort */ }

        return new AgentResult(
            Text: text,
            SessionId: extractedSessionId ?? "",
            ExitCode: proc.HasExited ? proc.ExitCode : -1,
            RawOutput: stdout.ToString());
    }

    // Scan a single JSON line for any of the session-id field shapes codex
    // has used across versions. Returns null if none found.
    public static string? ScanForSessionId(string line)
    {
        line = line.Trim();
        if (line.Length == 0 || line[0] != '{') return null;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch { return null; }

        using (doc)
        {
            var root = doc.RootElement;
            foreach (var key in new[] { "thread_id", "session_id", "sessionId" })
            {
                if (root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString();
                    if (s is not null && s.Length >= 8) return s;
                }
            }
            foreach (var (parent, child) in new[] { ("thread", "id"), ("session", "id") })
            {
                if (root.TryGetProperty(parent, out var p) && p.ValueKind == JsonValueKind.Object &&
                    p.TryGetProperty(child, out var v) && v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString();
                    if (s is not null && s.Length >= 8) return s;
                }
            }
            if (root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "thread_id", "session_id" })
                {
                    if (payload.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                    {
                        var s = v.GetString();
                        if (s is not null && s.Length >= 8) return s;
                    }
                }
            }
        }
        return null;
    }

    private static string QuoteIfNeeded(string arg)
    {
        if (arg.Length == 0) return "\"\"";
        var needs = arg.Any(c => char.IsWhiteSpace(c) || c == '"');
        if (!needs) return arg;
        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }
}
