using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using RemoteAgents.Events;
using RemoteAgents.Primitives;

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

    // Hook lifecycle for this agent. Default is the singleton installer
    // wrapping CodexHookConfig; replaceable via DI for tests. Agent base
    // invokes it iff Options.Hooks is non-null.
    public IHookInstaller<CodexAgent> HookInstaller { get; init; } = new CodexHookInstaller();

    public static List<string> BuildCodexArgs(string? sessionId, string projectDir, string lastMessageFile, CodexAgentOptions opts)
    {
        var args = new List<string>();
        if (sessionId is not null) { args.Add("exec"); args.Add("resume"); args.Add(sessionId); }
        else                        { args.Add("exec"); }

        args.Add("--cd"); args.Add(projectDir);
        args.Add("-o"); args.Add(lastMessageFile);
        args.Add("--sandbox"); args.Add(opts.Sandbox);
        // `codex exec` is autonomous by default — no approval-prompt
        // flag exists. The old `--dangerously-bypass-approvals-and-sandbox`
        // also disabled hook invocation (smoke confirmed hooks.jsonl
        // stayed empty under that mode); dropping it without replacement
        // gives us hooks-on autonomy.
        //
        // `--dangerously-bypass-hook-trust` skips the per-hook trust gate
        // for this invocation, so a freshly-installed ~/.codex/hooks.json
        // runs without an out-of-band trust prompt.
        args.Add("--dangerously-bypass-hook-trust");
        // The orchestrator explicitly knows the project dir; codex's
        // default trust-check refuses to run in non-git dirs (or
        // first-time-seen ones). The old --dangerously-bypass-approvals
        // flag silently covered this; now we're explicit.
        args.Add("--skip-git-repo-check");
        args.Add("--json");
        if (!string.IsNullOrEmpty(opts.Model)) { args.Add("--model"); args.Add(opts.Model); }

        // `-` tells codex to read the prompt from stdin.
        args.Add("-");
        return args;
    }

    protected override async ValueTask<IAsyncDisposable?> InstallHooksAsync(
        AgentRunRequest req, CancellationToken ct)
    {
        if (Options.Hooks is null) return null;
        return await HookInstaller.InstallAsync(req, Options.Hooks.ShimPath, ct);
    }

    protected override async Task<AgentResult> ExecuteAsync(AgentRunRequest req, CancellationToken ct)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "agents-codex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var lastMessageFile = Path.Combine(tmpDir, "last.txt");

        var args = BuildCodexArgs(req.SessionId, req.ProjectDir, lastMessageFile, Options);
        var commandLine = "codex " + string.Join(' ', args.Select(Shell.QuoteArg));

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
        if (Options.Hooks is not null)
            psi.Environment["REMOTEAGENTS_HOOKS_JSONL"] = Options.Hooks.HooksJsonlPath;

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        string? extractedSessionId = req.SessionId;

        // Pump stream chunks through an unbounded channel so EmitAsync is
        // awaited in order on a single consumer task. The previous
        // `_ = Sink.EmitAsync(...)` would race with itself under chatty
        // output and silently swallow sink exceptions.
        var chunks = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true,
        });

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            stdout.AppendLine(e.Data);
            if (extractedSessionId is null)
            {
                var found = ScanForSessionId(e.Data);
                if (found is not null) extractedSessionId = found;
            }
            chunks.Writer.TryWrite(e.Data + "\n");
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) stderr.AppendLine(e.Data);
        };

        var pump = Task.Run(async () =>
        {
            await foreach (var chunk in chunks.Reader.ReadAllAsync())
            {
                await Sink.EmitAsync(new AgentEvent.StreamChunk(DateTimeOffset.UtcNow, Name, chunk));
            }
        });

        // System prompt prepended to user prompt — codex has no flag for it.
        var effectiveSystemPrompt = UnattendedDirective.Compose(Options.SystemPrompt, req.Mode);
        var fullPrompt = string.IsNullOrEmpty(effectiveSystemPrompt)
            ? req.Prompt
            : effectiveSystemPrompt + "\n\n" + req.Prompt;

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

        // Drain any chunks queued between WaitForExit returning and the
        // OutputDataReceived callbacks finishing.
        chunks.Writer.Complete();
        await pump;

        string text = "";
        if (File.Exists(lastMessageFile))
            text = await File.ReadAllTextAsync(lastMessageFile, ct);

        try { Directory.Delete(tmpDir, recursive: true); } catch { /* best-effort */ }

        var outcome = HookResolution.FromHooksJsonl(
            Options.Hooks?.HooksJsonlPath, new CodexHookParser(), req.Mode);

        // Defense in depth: codex exec's `-o` output mirrors what would
        // land in a Stop hook's last_assistant_message. If hooks didn't
        // fire (config-shape issues, version regressions, or an
        // accidentally-restored "skip hooks" flag), the sentinel /
        // heuristic still surface from text. Source tags carry `.text`
        // so the UI can distinguish "from hook" vs "from output file".
        if (outcome.Question is null)
        {
            var fromText = StopPayloadInspector.InspectText(
                text,
                sentinelSource:  "codex.text.sentinel",
                heuristicSource: "codex.text.heuristic");
            if (fromText is not null)
                outcome = HookResolution.ForQuestion(fromText, req.Mode);
        }

        if (outcome.Status == AgentStatus.Failed && outcome.Question is not null)
        {
            await Sink.EmitAsync(new AgentEvent.NonInteractiveViolation(
                DateTimeOffset.UtcNow, Name, outcome.Question.Source, outcome.Question.Text),
                CancellationToken.None);
        }

        return new AgentResult(
            Text:          text,
            SessionId:     extractedSessionId ?? "",
            ExitCode:      proc.HasExited ? proc.ExitCode : -1,
            RawOutput:     stdout.ToString(),
            Status:        outcome.Status,
            Question:      outcome.Question,
            FailureReason: outcome.FailureReason);
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
}
