using System.Text;
using Porta.Pty;
using RemoteAgents.Events;
using RemoteAgents.Pty;

namespace RemoteAgents.Agents;

// Drives `claude` CLI inside ConPTY so isatty() === true in the child
// process — that's what keeps the call on Max subscription billing instead
// of the API path. Q12: Windows-only v1 (cmd.exe /c).
//
// Non-sealed (Q7) with two virtual hooks for v1 (Q8): DetectStartupDialog
// and IsResponseComplete. Other private methods can be lifted to virtual
// if a real subclass need shows up.
public class ClaudeAgent : Agent
{
    public ClaudeAgentOptions Options { get; init; } = new();

    public int Cols { get; init; } = 120;
    public int Rows { get; init; } = 40;

    // Hooks — override per-project if Claude changes its UI.
    protected virtual string? DetectStartupDialog(string buf)
    {
        var plain = AnsiHelpers.StripAnsi(buf);
        if (plain.Contains("Bypass Permissions mode", StringComparison.Ordinal) ||
            plain.Contains("Yes, I accept", StringComparison.Ordinal))
            return "bypass-warning";
        if (plain.Contains("trust this folder", StringComparison.OrdinalIgnoreCase) ||
            plain.Contains("Is this a project you", StringComparison.OrdinalIgnoreCase))
            return "trust";
        return null;
    }

    protected virtual bool IsResponseComplete(string buf, DateTimeOffset lastChunkAt)
        => (DateTimeOffset.UtcNow - lastChunkAt).TotalMilliseconds > Options.IdleThresholdMs;

    // Public for testing — exact arg list claude will be invoked with.
    public static List<string> BuildClaudeArgs(string effectiveSessionId, bool isResume, ClaudeAgentOptions opts)
    {
        var args = new List<string>();
        if (isResume) { args.Add("--resume"); args.Add(effectiveSessionId); }
        else          { args.Add("--session-id"); args.Add(effectiveSessionId); }
        if (!string.IsNullOrEmpty(opts.PermissionMode))
        {
            args.Add("--permission-mode");
            args.Add(opts.PermissionMode);
        }
        if (!string.IsNullOrEmpty(opts.Model))
        {
            args.Add("--model");
            args.Add(opts.Model);
        }
        if (!string.IsNullOrEmpty(opts.SystemPrompt))
        {
            args.Add("--append-system-prompt");
            args.Add(opts.SystemPrompt);
        }
        return args;
    }

    protected override async Task<AgentResult> ExecuteAsync(AgentRunRequest req, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.Prompt)) throw new ArgumentException("prompt is required", nameof(req));
        if (string.IsNullOrEmpty(req.ProjectDir)) throw new ArgumentException("projectDir is required", nameof(req));

        var effectiveSessionId = req.SessionId ?? Guid.NewGuid().ToString();
        var isResume = req.SessionId is not null;
        var claudeArgs = BuildClaudeArgs(effectiveSessionId, isResume, Options);

        // Windows-only v1: cmd.exe /c claude <args...>. PtyOptions.Environment
        // is passed verbatim — we explicitly blank out the API-key vars to
        // keep subscription billing intact (defense in depth: SubscriptionGuard
        // already refused to start if any were set).
        var env = new Dictionary<string, string>();
        foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
        {
            if (kv.Key is string k && kv.Value is string v) env[k] = v;
        }
        env["ANTHROPIC_API_KEY"] = "";
        env["CLAUDE_API_KEY"] = "";

        // Smoke-test-proven shape: spawn cmd.exe in cwd; write the claude
        // launch line to its stdin. Letting the shell parse the command keeps
        // arg quoting aligned with the JS provider behavior under cmd.exe.
        var launchLine = "claude " + string.Join(' ', claudeArgs.Select(QuoteIfNeeded)) + "\r";
        var ptyOpts = new PtyOptions
        {
            Name = "claude-agent",
            Cols = Cols,
            Rows = Rows,
            Cwd = req.ProjectDir,
            App = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Environment = env,
        };

        using var pty = await PtyProvider.SpawnAsync(ptyOpts, ct);

        var buffer = new StringBuilder();
        var bufLock = new object();
        var lastChunkAt = DateTimeOffset.UtcNow;
        var exited = false;
        // Two cancellation sources:
        //   - readerCts: linked to caller ct. Cancelled only on Kill-path
        //     teardown so a still-blocked Read doesn't keep the agent
        //     hanging. On the happy path, the PTY ReaderStream closes
        //     when cmd.exe exits → ReadAsync returns 0 → loop exits
        //     cleanly without us ever cancelling. That guarantees no
        //     trailing bytes get dropped from the buffer.
        //   - sinkCts: a separate token for sink emits so we don't mark
        //     them cancelled when we shut the reader down.
        using var readerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var readerTask = Task.Run(async () =>
        {
            var buf = new byte[4096];
            try
            {
                while (true)
                {
                    var n = await pty.ReaderStream.ReadAsync(buf, 0, buf.Length, readerCts.Token);
                    if (n == 0) break; // PTY closed — clean EOF, drained.
                    var chunk = Encoding.UTF8.GetString(buf, 0, n);
                    lock (bufLock) buffer.Append(chunk);
                    lastChunkAt = DateTimeOffset.UtcNow;
                    await Sink.EmitAsync(new AgentEvent.StreamChunk(DateTimeOffset.UtcNow, Name, chunk), CancellationToken.None);
                }
            }
            catch (OperationCanceledException) { /* Kill-path teardown */ }
            catch (IOException) { /* PTY stream torn down */ }
        });

        // Give cmd.exe a moment to render its prompt, then launch claude.
        await Task.Delay(500, ct);
        await WriteAsync(pty, launchLine, ct);

        // ── 1. initial UI render dwell — claude takes a few seconds to draw
        await Task.Delay(Options.InitialDwellMs, ct);

        // ── 2. startup dialog dismissal
        string snapshot;
        lock (bufLock) snapshot = buffer.ToString();
        var dialog = DetectStartupDialog(snapshot);
        if (dialog == "trust")
        {
            await WriteAsync(pty, "\r", ct);
            await Sink.EmitAsync(new AgentEvent.DialogDismissed(DateTimeOffset.UtcNow, Name, "trust"), ct);
            await Task.Delay(Options.InitialDwellMs / 2, ct);
        }
        else if (dialog == "bypass-warning")
        {
            await WriteAsync(pty, "2\r", ct);
            await Sink.EmitAsync(new AgentEvent.DialogDismissed(DateTimeOffset.UtcNow, Name, "bypass-warning"), ct);
            await Task.Delay(Options.InitialDwellMs / 2, ct);
        }

        // ── 3. type prompt + submit
        await WriteAsync(pty, req.Prompt, ct);
        await Task.Delay(500, ct);
        await WriteAsync(pty, "\r", ct);

        // ── 4. wait for response to settle (idle for IdleThresholdMs)
        var submittedAt = DateTimeOffset.UtcNow;
        while (!exited && (DateTimeOffset.UtcNow - submittedAt).TotalMilliseconds < Options.MaxWaitMs)
        {
            await Task.Delay(500, ct);
            string snap2;
            lock (bufLock) snap2 = buffer.ToString();
            if (IsResponseComplete(snap2, lastChunkAt)) break;
        }

        // ── 5. /exit so claude prints the resume URL, then exit cmd itself
        await WriteAsync(pty, "/exit\r", ct);
        await Task.Delay(Options.ExitDwellMs, ct);
        await WriteAsync(pty, "exit\r", ct);
        exited = pty.WaitForExit(15_000);

        int exitCode;
        if (exited)
        {
            // Happy path: cmd.exe exited → PTY stream will close → reader
            // loop sees ReadAsync==0 and exits cleanly. Give it a short
            // grace window to drain, but do NOT cancel — that would risk
            // truncating Claude's final bytes.
            var drained = await Task.WhenAny(readerTask, Task.Delay(2000));
            if (drained != readerTask)
            {
                // PTY didn't close its stream within 2s of exit. Force the
                // reader down so we can return.
                readerCts.Cancel();
                try { await readerTask; } catch { }
            }
            exitCode = pty.ExitCodeOrNull() ?? 0;
        }
        else
        {
            // PTY didn't exit. Kill, drain best-effort, surface exit code -1
            // to signal the abnormal teardown to the caller / Completed event.
            try { pty.Kill(); } catch { /* best-effort */ }
            readerCts.Cancel();
            try { await readerTask; } catch { }
            exitCode = pty.ExitCodeOrNull() ?? -1;
            if (exitCode == 0) exitCode = -1; // Kill path is never "success"
        }

        var raw = buffer.ToString();

        // Prefer the per-session JSONL Claude writes — it survives TUI
        // re-wraps, ANSI noise, and any reader-drain truncation. Fall back
        // to the ANSI-stripped buffer if the file isn't there yet.
        var jsonlText = ClaudeJsonl.TryReadLastAssistantText(req.ProjectDir, effectiveSessionId, req.Prompt);
        var text = !string.IsNullOrWhiteSpace(jsonlText)
            ? jsonlText!
            : ExtractAssistantText(raw, req.Prompt);

        return new AgentResult(
            Text: text,
            SessionId: effectiveSessionId,
            ExitCode: exitCode,
            RawOutput: raw);
    }

    private static async Task WriteAsync(IPtyConnection pty, string s, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        await pty.WriterStream.WriteAsync(bytes, 0, bytes.Length, ct);
        await pty.WriterStream.FlushAsync(ct);
    }

    private static string QuoteIfNeeded(string arg)
    {
        if (arg.Length == 0) return "\"\"";
        var needs = arg.Any(c => char.IsWhiteSpace(c) || c == '"');
        if (!needs) return arg;
        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }

    private static string ExtractAssistantText(string buf, string prompt)
    {
        var plain = AnsiHelpers.StripAnsi(buf);
        var idx = plain.IndexOf(prompt, StringComparison.Ordinal);
        if (idx < 0) return "";
        var tail = plain[(idx + prompt.Length)..];
        var next = tail.IndexOf("\n> ", StringComparison.Ordinal);
        if (next > 0) tail = tail[..next];
        return tail.Trim();
    }
}
