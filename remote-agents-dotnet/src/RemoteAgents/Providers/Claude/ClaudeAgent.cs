using RemoteAgents.Agents;
using Porta.Pty;
using RemoteAgents.Agents.Hooks;
using RemoteAgents.Events;
using RemoteAgents.Primitives;
using RemoteAgents.Providers.Claude.Terminal;
using RemoteAgents.Runs;

namespace RemoteAgents.Providers.Claude;

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
    public ClaudeAgent() : base("claude") { }

    public ClaudeAgentOptions Options { get; init; } = new();
    protected override AgentOptions BaseOptions => Options;

    public int Cols { get; init; } = 120;
    public int Rows { get; init; } = 40;

    // Hooks — override per-project if Claude changes its UI.
    protected virtual StartupDialog? DetectStartupDialog(string buf)
    {
        var plain = AnsiHelpers.StripAnsi(buf);
        if (plain.Contains("Bypass Permissions mode", StringComparison.Ordinal) ||
            plain.Contains("Yes, I accept", StringComparison.Ordinal))
            return StartupDialog.BypassWarning;
        if (plain.Contains("trust this folder", StringComparison.OrdinalIgnoreCase) ||
            plain.Contains("Is this a project you", StringComparison.OrdinalIgnoreCase))
            return StartupDialog.Trust;
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

    // Hook wiring consumed by the Agent base. Resolution + violation
    // emission live in the base; this getter declares what to install,
    // how to tear it down, and how to parse the resulting hooks.jsonl.
    protected override HookIntegration? Hooks => Options.Hooks is null ? null
        : new HookIntegration(
            HooksJsonlPath: Options.Hooks.HooksJsonlPath,
            Parser:         new ClaudeHookParser(),
            Install:        req =>
            {
                ClaudeHookConfig.Install(req.ProjectDir, Options.Hooks.ShimPath);
                return () => ClaudeHookConfig.Uninstall(req.ProjectDir);
            });

    protected override async Task<DriveResult> DriveAsync(AgentDriveContext ctx, CancellationToken ct)
    {
        var req = ctx.Request;
        var sessionId = req.SessionId ?? Guid.NewGuid().ToString();
        var effectiveOpts = Options with { SystemPrompt = ctx.SystemPrompt };
        var claudeArgs = BuildClaudeArgs(sessionId, isResume: req.SessionId is not null, effectiveOpts);
        var launchLine = "claude " + string.Join(' ', claudeArgs.Select(Shell.QuoteArg));

        // Announce the provider session id on the channel before the PTY
        // starts so a SignalR listener attaching after Started has the
        // ProviderSessionRef sitting in replay.
        await Sink.EmitAsync(new AgentEvent.ProviderSessionAttached(
            DateTimeOffset.UtcNow, Name, new ProviderSessionRef("claude", sessionId)), ct);

        // Hard deadline: even if WaitIdle hangs, /exit is ignored, or the
        // PTY never EOFs, this CTS fires and PtySession's DisposeAsync
        // (Cancel reader → Kill PTY → close Job Object) tears everything
        // down on the way out of the using block. Without this, a wedged
        // claude could pin the orchestrator indefinitely — exactly the
        // failure mode that left JS-prototype zombies running for weeks.
        using var deadlineCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(Options.MaxOverallMs));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, deadlineCts.Token);
        var dct = linkedCts.Token;

        // Live-tail Claude's per-session JSONL into the sink as typed chat
        // events (AssistantText / Thinking / ToolUse / ToolResult / ...).
        // emitterCts is signaled by US (after the PTY has fully drained)
        // so the emitter gets a chance to read the final flush before
        // exiting; dct kills it on hard deadline / external cancel.
        using var emitterCts = CancellationTokenSource.CreateLinkedTokenSource(dct);
        var emitter = new ClaudeJsonlEmitter(req.ProjectDir, sessionId, Name, Sink);
        var emitterTask = Task.Run(() => emitter.RunAsync(emitterCts.Token), dct);

        var pty = await SpawnPtyAsync(BuildPtyOptions(req.ProjectDir, ctx.HooksJsonlPath), dct);
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

        // Grace window for Claude's final-flush JSONL writes after the PTY
        // closes (it sometimes lags by ~100ms), then stop the emitter and
        // wait for it to drain. Swallow faults — the emitter is best-effort.
        try { await Task.Delay(400, dct); } catch (OperationCanceledException) { }
        emitterCts.Cancel();
        try { await emitterTask; } catch { /* shutdown / IO transients */ }

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

    private PtyOptions BuildPtyOptions(string projectDir, string? hooksJsonlPath)
    {
        // PtyOptions.Environment is passed verbatim — blank the API-key
        // vars (centralized in EnvScrub) on the child as defense in depth
        // against a SubscriptionGuard regression.
        var env = new Dictionary<string, string>();
        foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
        {
            if (kv.Key is string k && kv.Value is string v) env[k] = v;
        }
        foreach (var k in EnvScrub.SubscriptionKeys) env[k] = "";
        if (hooksJsonlPath is not null)
            env["REMOTEAGENTS_HOOKS_JSONL"] = hooksJsonlPath;

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
            StartupDialog.Trust          => "\r",
            StartupDialog.BypassWarning  => "2\r",
            _                            => null,
        };
        if (keys is null) return;

        await session.WriteAsync(keys, ct);
        // Emit the wire-stable label so transcript readers don't need to
        // care about the enum's serialization. (Match wording matches the
        // old stringly-typed return for back-compat.)
        string label = dialog == StartupDialog.Trust ? "trust" : "bypass-warning";
        await Sink.EmitAsync(new AgentEvent.DialogDismissed(DateTimeOffset.UtcNow, Name, label), ct);
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
