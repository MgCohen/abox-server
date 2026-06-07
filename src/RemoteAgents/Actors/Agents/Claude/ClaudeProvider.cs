using Porta.Pty;
using RemoteAgents.Tools.CommandLine;

namespace RemoteAgents.Actors.Agents.Claude;

public sealed class ClaudeProvider(ClaudeConfig config, IQuestionResolver resolver) : IProvider
{
    private const int Cols = 120;
    private const int Rows = 40;
    private const int PollMs = 150;
    private const int StartupCapMs = 30_000;           // cold start + plugins + remote-control attach
    private const int ReadySettleMs = 1_200;
    private const int SubmitSettleMs = 500;            // oracle A5: anti-paste pause
    private const int ResponseStopPollMs = 500;
    private const int ResponseCapMs = 5 * 60_000;
    private const int ResolveTimeoutMs = 2_000;
    private const int ResolvePollMs = 100;
    private const int MaxOverallMs = 10 * 60_000;      // oracle A10: wall-clock cap → guaranteed teardown

    public async Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct)
    {
        await SubscriptionGuard.CheckAsync(EnvScrub.SubscriptionKeys, "claude", ct);

        var isResume = request.SessionId is not null;
        var sessionId = request.SessionId ?? Guid.NewGuid().ToString();
        var systemPromptFile = WriteSystemPromptFile(AgentDirective.ComposeSystemPrompt(config.SystemPrompt));
        using var hook = ClaudeHooks.Create(gatePermissions: config.Policy != PermissionPolicy.Bypass);
        try
        {
            var permissionMode = ClaudeProtocol.PermissionMode(config.Policy);
            var args = ClaudeProtocol.BuildArgs(sessionId, isResume, permissionMode, config.Model, systemPromptFile, hook.SettingsFile);
            var launchLine = "claude " + string.Join(' ', args.Select(Shell.QuoteArg));

            using var deadlineCts = new CancellationTokenSource(MaxOverallMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, deadlineCts.Token);
            var dct = linkedCts.Token;

            var pty = await PtyProvider.SpawnAsync(BuildPtyOptions(request.ProjectDir, hook), dct);
            await using var session = new PtySession(pty, dct);

            await session.WriteLineAsync(launchLine, dct);
            await DismissStartupDialogsAsync(session, dct);

            // Wait for the input-bar marker, not for silence: a mid-startup re-render is indistinguishable from a settled idle.
            if (!await session.WaitUntilAsync(ClaudeProtocol.IsPromptReady, ReadySettleMs, StartupCapMs, PollMs, dct))
                throw new InvalidOperationException("Claude input bar did not become ready in time; cannot submit the prompt.");

            await session.SubmitAsync(request.Prompt, SubmitSettleMs, dct);
            await PumpUntilStopAsync(hook, dct);

            // The Stop hook fired: the final message and JSONL transcript are
            // already on disk. Read them, then let `await using` dispose kill the
            // PTY tree (Job Object cascade, oracle A10) — no graceful /exit needed,
            // and the cmd exit code was never the turn outcome anyway.
            var text = hook.ReadFinalMessage();
            if (string.IsNullOrWhiteSpace(text))
                text = await ResolveTextFallbackAsync(sessionId, request.Prompt, dct);
            var transcript = ClaudeJsonl.TryReadLastTurnTranscript(sessionId, request.Prompt) ?? [];
            var exitCode = hook.HasFired || !string.IsNullOrWhiteSpace(text) ? 0 : 1;
            return new DriveResult(text ?? "", sessionId, exitCode, session.Buffer, transcript);
        }
        finally
        {
            TryDelete(systemPromptFile);
        }
    }

    // The launch line is typed into cmd.exe through the PTY, so the (multiline)
    // system prompt must travel as a file path, not an inline arg (oracle A-Win).
    private static string WriteSystemPromptFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"claude-sysprompt-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { /* best-effort: temp cleanup is non-fatal */ }
    }

    // Dismiss each dialog kind at most once: a repeated keystroke would land in the next screen and corrupt the prompt.
    private static async Task DismissStartupDialogsAsync(PtySession session, CancellationToken ct)
    {
        var dismissed = new HashSet<StartupDialog>();
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(StartupCapMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (ClaudeProtocol.IsPromptReady(session.Buffer)) return;

            if (ClaudeProtocol.DetectStartupDialog(session.Buffer) is { } dialog && dismissed.Add(dialog))
                await session.WriteAsync(DialogKeys(dialog), ct);

            await Task.Delay(PollMs, ct);
        }
    }

    private static string DialogKeys(StartupDialog dialog) => dialog switch
    {
        StartupDialog.Trust => "\r",
        StartupDialog.BypassWarning => "2\r",
        _ => "",
    };

    // Pump the turn: the Stop hook fires once when Claude truly ends (oracle
    // A-Stop), but a gated tool can raise a mid-turn PreToolUse request first.
    // Drain and resolve each before treating Stop as terminal (plan §6).
    private async Task PumpUntilStopAsync(ClaudeHooks hook, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(ResponseCapMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            foreach (var pending in hook.DrainRequests())
                await ResolvePermissionAsync(hook, pending, ct);

            if (hook.HasFired) return;
            try { await Task.Delay(ResponseStopPollMs, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    // Auto applies an automatic policy with no human in the loop. v1 minimal:
    // auto-approve every gated tool (the allowlist/denylist engine is the deferred
    // refinement, ADR 0007 §6 — the "second use" that would earn an AutoResolver).
    // Ask routes to the resolver; an unresolvable call (null) denies — the safe,
    // non-hanging default that replaces acceptEdits' silent mid-turn block.
    private async Task ResolvePermissionAsync(ClaudeHooks hook, PermissionRequest request, CancellationToken ct)
    {
        if (config.Policy == PermissionPolicy.Auto)
        {
            hook.Respond(request, ClaudePermission.RenderResponse(allow: true, "auto-approved"));
            return;
        }

        var allow = ClaudePermission.IsAllow(await resolver.ResolveAsync(ClaudePermission.ToQuestion(request), ct));
        hook.Respond(request, ClaudePermission.RenderResponse(allow, allow ? "approved" : "denied (no approval)"));
    }

    // Fallback only: if the Stop hook never delivered the final text, recover it
    // from the per-session JSONL (oracle A6), polling briefly for the flush lag.
    private static async Task<string> ResolveTextFallbackAsync(string sessionId, string prompt, CancellationToken ct)
    {
        for (var waited = 0; waited <= ResolveTimeoutMs; waited += ResolvePollMs)
        {
            var text = ClaudeJsonl.TryReadLastAssistantText(sessionId, prompt);
            if (!string.IsNullOrWhiteSpace(text)) return text;
            try { await Task.Delay(ResolvePollMs, ct); }
            catch (OperationCanceledException) { break; }
        }
        return "";
    }

    private static PtyOptions BuildPtyOptions(string projectDir, ClaudeHooks hook)
    {
        var env = new Dictionary<string, string>();
        foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
            if (kv.Key is string k && kv.Value is string v) env[k] = v;
        foreach (var key in EnvScrub.SubscriptionKeys) env[key] = "";
        env[ClaudeHooks.SignalEnvVar] = hook.SignalFile;
        if (hook.PermissionDir is not null) env[ClaudeHooks.PermissionEnvVar] = hook.PermissionDir;

        return new PtyOptions
        {
            Name = "claude-agent",
            Cols = Cols,
            Rows = Rows,
            Cwd = projectDir,
            App = Shell.Executable,
            Environment = env,
        };
    }
}
