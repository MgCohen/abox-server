using Porta.Pty;
using RemoteAgents.Events;
using RemoteAgents.Primitives;
using RemoteAgents.Pty;

namespace RemoteAgents.Agents;

// Drives `claude` CLI inside ConPTY so isatty() === true in the child
// process — that's what keeps the call on Max subscription billing instead
// of the API path. Q12: Windows-only v1 (cmd.exe /c).
//
// Non-sealed (Q7) with two virtual hooks for v1 (Q8): DetectStartupDialog
// and IsResponseComplete. The PTY plumbing (buffer, reader task,
// drain/kill) lives in PtySession; this class only owns the script.
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

    // PTY spawn hook. Defaults to PtyProvider.SpawnAsync (real ConPTY).
    // Override in tests with a FakePtyConnection so ExecuteAsync's drive
    // loop can be exercised without launching `claude` for real.
    protected virtual Task<IPtyConnection> SpawnPtyAsync(PtyOptions opts, CancellationToken ct)
        => PtyProvider.SpawnAsync(opts, ct);

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
        var sessionId = req.SessionId ?? Guid.NewGuid().ToString();
        var claudeArgs = BuildClaudeArgs(sessionId, isResume: req.SessionId is not null, Options);
        var launchLine = "claude " + string.Join(' ', claudeArgs.Select(Shell.QuoteArg));

        // Hard deadline: even if WaitIdle hangs, /exit is ignored, or the
        // PTY never EOFs, this CTS fires and PtySession's DisposeAsync
        // (Cancel reader → Kill PTY → close Job Object) tears everything
        // down on the way out of the using block. Without this, a wedged
        // claude could pin the orchestrator indefinitely — exactly the
        // failure mode that left JS-prototype zombies running for weeks.
        using var deadlineCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(Options.MaxOverallMs));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, deadlineCts.Token);
        var dct = linkedCts.Token;

        var pty = await SpawnPtyAsync(BuildPtyOptions(req.ProjectDir), dct);
        await using var session = new PtySession(
            pty,
            onChunk: (chunk, innerCt) => Sink.EmitAsync(
                new AgentEvent.StreamChunk(DateTimeOffset.UtcNow, Name, chunk), innerCt),
            dct);

        // 1. Give cmd.exe a moment to render, then launch claude + dwell
        //    for its first paint.
        await session.DwellAsync(500, dct);
        await session.WriteLineAsync(launchLine, dct);
        await session.DwellAsync(Options.InitialDwellMs, dct);

        // 2. Startup dialog dismissal (trust folder / bypass warning).
        await MaybeDismissDialogAsync(session, dct);

        // 3. Type the prompt and submit.
        await session.WriteAsync(req.Prompt, dct);
        await session.DwellAsync(500, dct);
        await session.WriteAsync("\r", dct);

        // 4. Wait for Claude's response to settle (no new chunks for
        //    IdleThresholdMs, capped at MaxWaitMs).
        await session.WaitIdleAsync(Options.IdleThresholdMs, Options.MaxWaitMs, ct: dct);

        // 5. Send /exit so claude prints the resume URL, then exit cmd.
        await session.WriteLineAsync("/exit", dct);
        await session.DwellAsync(Options.ExitDwellMs, dct);
        await session.WriteLineAsync("exit", dct);
        var exitCode = await session.ShutdownAsync(Options.WaitForExitMs, Options.ReaderDrainMs);

        var raw = session.Buffer;

        // Prefer the per-session JSONL Claude writes — it survives TUI
        // re-wraps, ANSI noise, and any reader-drain truncation. Fall back
        // to the ANSI-stripped buffer if the file isn't there yet.
        var jsonlText = ClaudeJsonl.TryReadLastAssistantText(req.ProjectDir, sessionId, req.Prompt);
        var text = !string.IsNullOrWhiteSpace(jsonlText)
            ? jsonlText!
            : ExtractAssistantText(raw, req.Prompt);

        return new AgentResult(Text: text, SessionId: sessionId, ExitCode: exitCode, RawOutput: raw);
    }

    private PtyOptions BuildPtyOptions(string projectDir)
    {
        // PtyOptions.Environment is passed verbatim — explicitly blank out
        // the API-key vars to keep subscription billing intact (defense in
        // depth: SubscriptionGuard already refused to start if any were set).
        var env = new Dictionary<string, string>();
        foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
        {
            if (kv.Key is string k && kv.Value is string v) env[k] = v;
        }
        env["ANTHROPIC_API_KEY"] = "";
        env["CLAUDE_API_KEY"] = "";

        return new PtyOptions
        {
            Name = "claude-agent",
            Cols = Cols,
            Rows = Rows,
            Cwd = projectDir,
            App = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Environment = env,
        };
    }

    private async Task MaybeDismissDialogAsync(PtySession session, CancellationToken ct)
    {
        var dialog = DetectStartupDialog(session.Buffer);
        if (dialog is null) return;

        var keys = dialog switch
        {
            "trust"          => "\r",
            "bypass-warning" => "2\r",
            _                => null,
        };
        if (keys is null) return;

        await session.WriteAsync(keys, ct);
        await Sink.EmitAsync(new AgentEvent.DialogDismissed(DateTimeOffset.UtcNow, Name, dialog), ct);
        await session.DwellAsync(Options.InitialDwellMs / 2, ct);
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
