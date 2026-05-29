using Porta.Pty;
using RemoteAgents.Events;
using RemoteAgents.Primitives;
using RemoteAgents.Pty;

namespace RemoteAgents.Agents;

// Drives `claude` CLI inside ConPTY so isatty() === true in the child
// process — that's what keeps the call on Max subscription billing instead
// of the API path. Q12: Windows-only v1 (cmd.exe /c).
//
// Non-sealed (Q7) with two protected virtual hooks: DetectStartupDialog
// (so projects can recognize new TUI dialog wording) and SpawnPtyAsync
// (so tests can swap in a fake PTY). Idle/wait/dwell budgets live on
// ClaudeAgentOptions, not on virtual hooks. The PTY plumbing (buffer,
// reader task, idle-wait, drain/kill) lives in PtySession; this class
// only owns the script.
public class ClaudeAgent : Agent
{
    public ClaudeAgentOptions Options { get; init; } = new();

    // Hook lifecycle for this agent. Default is the singleton installer
    // wrapping ClaudeHookConfig; replaceable via DI for tests. Agent base
    // invokes it iff Options.Hooks is non-null.
    public IHookInstaller<ClaudeAgent> HookInstaller { get; init; } = new ClaudeHookInstaller();

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
    // Override in tests with a FakePtyConnection so DriveAsync's drive
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

    // Hook plumbing consumed by the Agent base. Resolution + violation
    // emission live in the base; this class only declares what to install
    // and how to parse the resulting hooks.jsonl.
    protected override HookIntegrationOptions? HookConfig => Options.Hooks;
    protected override IAgentHookParser? HookParser => new ClaudeHookParser();
    protected override Task<IAsyncDisposable> InstallHookScopeAsync(
        AgentRunRequest req, string shimPath, CancellationToken ct)
        => HookInstaller.InstallAsync(req, shimPath, ct);

    protected override async Task<DriveResult> DriveAsync(AgentRunRequest req, CancellationToken ct)
    {
        var sessionId = req.SessionId ?? Guid.NewGuid().ToString();
        var effectiveOpts = Options with
        {
            SystemPrompt = UnattendedDirective.Compose(Options.SystemPrompt, req.Mode)
        };
        var claudeArgs = BuildClaudeArgs(sessionId, isResume: req.SessionId is not null, effectiveOpts);
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

        // 1. Launch claude and wait for its splash to settle. cmd.exe's
        //    stdin buffers, so no boot dwell needed before the WriteLine.
        //    The minWaitMs floor is load-bearing: there's a ~2s silent gap
        //    between cmd.exe echoing the `claude` command and claude.exe
        //    starting to paint, which a pure idle-only wait will mistake
        //    for a settled TUI and proceed to type the prompt before
        //    claude is ready to read it.
        await session.WriteLineAsync(launchLine, dct);
        await session.WaitIdleAsync(
            Options.LaunchSettleIdleMs,
            maxWaitMs: 8_000,
            minWaitMs: Options.LaunchSettleMinWaitMs,
            ct: dct);

        // 2. Startup dialog dismissal (trust folder / bypass warning).
        await MaybeDismissDialogAsync(session, dct);

        // 3. Type the prompt, pause so Claude's TUI registers it as a
        //    typed submit (not a bracketed paste), then press Enter.
        await session.SubmitAsync(req.Prompt, settleMs: 500, ct: dct);

        // 4. Wait for Claude's response to settle.
        await session.WaitIdleAsync(Options.IdleThresholdMs, Options.MaxWaitMs, ct: dct);

        // 5. Tell claude to leave, wait for its goodbye, then exit cmd.
        await session.WriteLineAsync("/exit", dct);
        await session.WaitIdleAsync(Options.ExitSettleIdleMs, maxWaitMs: 5_000, ct: dct);
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

        return new DriveResult(Text: text, SessionId: sessionId, ExitCode: exitCode, RawOutput: raw);
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
        if (Options.Hooks is not null)
            env["REMOTEAGENTS_HOOKS_JSONL"] = Options.Hooks.HooksJsonlPath;

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
        // Wait for claude to transition into its main UI before the
        // caller starts typing the prompt.
        await session.WaitIdleAsync(Options.LaunchSettleIdleMs, maxWaitMs: 5_000, ct: ct);
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
