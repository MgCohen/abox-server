using Porta.Pty;
using ABox.Infrastructure.CommandLine;
using ABox.Infrastructure.Sandbox;

namespace ABox.Domain.Agents.Claude;

public sealed class ClaudeProvider(ClaudeConfig config, IDecisionResolver resolver, AutoPolicy autoPolicy, SandboxSettings sandbox) : IProvider
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

        using var hook = ClaudeHooks.Create(gatePermissions: config.Policy != PermissionPolicy.Bypass, boxDir: DockerSandbox.SessionMount);

        // The system prompt travels as a file on the /session mount (oracle A-Win: a
        // multiline prompt can't be an inline arg), referenced by its in-box path.
        var sysPromptName = $"sysprompt-{Guid.NewGuid():N}.txt";
        File.WriteAllText(Path.Combine(hook.HostDir, sysPromptName),
            AgentDirective.ComposeSystemPrompt(config.SystemPrompt, config.Resolution));
        var sysPromptInBox = $"{DockerSandbox.SessionMount}/{sysPromptName}";

        var home = PrepareHome();
        try
        {
            var permissionMode = ClaudeProtocol.PermissionMode(config.Policy);
            var args = ClaudeProtocol.BuildArgs(sessionId, isResume, permissionMode, config.Model, sysPromptInBox, hook.SettingsPathInBox);
            var claudeLine = "claude " + string.Join(' ', args.Select(Shell.QuoteArg));

            using var deadlineCts = new CancellationTokenSource(MaxOverallMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, deadlineCts.Token);
            var dct = linkedCts.Token;

            var options = new SandboxOptions(
                Worktree: new DirectoryInfo(request.ProjectDir),
                SessionDir: new DirectoryInfo(hook.HostDir),
                Home: home,
                Image: sandbox.Image,
                Network: sandbox.Network);
            await using var box = await DockerSandbox.OpenAsync(options, dct);

            var launchLine = box.InteractiveExecLine(claudeLine, BoxEnv(hook));

            var pty = await PtyProvider.SpawnAsync(BuildPtyOptions(), dct);
            await using var session = new PtySession(pty, dct);

            await session.WriteLineAsync(launchLine, dct);
            await DismissStartupDialogsAsync(session, dct);

            // Wait for the input-bar marker, not for silence: a mid-startup re-render is indistinguishable from a settled idle.
            if (!await session.WaitUntilAsync(ClaudeProtocol.IsPromptReady, ReadySettleMs, StartupCapMs, PollMs, dct))
                throw new InvalidOperationException("Claude input bar did not become ready in time; cannot submit the prompt.");

            await session.SubmitAsync(request.Prompt, SubmitSettleMs, dct);
            await PumpUntilStopAsync(hook, dct);

            // The Stop hook fired across the /session mount: the final message and the
            // JSONL (under the mounted HOME) are on disk. Read them, then let dispose
            // tear down the box (docker rm -f, oracle A10) and kill the host PTY.
            var projectsRoot = Path.Combine(home.FullName, ".claude", "projects");
            var text = hook.ReadFinalMessage();
            if (string.IsNullOrWhiteSpace(text))
                text = await ResolveTextFallbackAsync(sessionId, request.Prompt, projectsRoot, dct);
            var transcript = ClaudeJsonl.TryReadLastTurnTranscript(sessionId, request.Prompt, projectsRoot) ?? [];
            var exitCode = hook.HasFired || !string.IsNullOrWhiteSpace(text) ? 0 : 1;
            return new DriveResult(text ?? "", sessionId, exitCode, session.Buffer, transcript);
        }
        finally
        {
            TryDeleteDir(home);
        }
    }

    // Provisional: the credential + onboarding state come from the pre-onboarded
    // template HOME. Until the owner provisions a setup-token home, the box has no
    // credential and a real turn can't authenticate — the deferred validation step.
    private DirectoryInfo PrepareHome()
    {
        var home = Directory.CreateTempSubdirectory("ra-claude-home-");
        if (sandbox.TemplateHome is { Exists: true } template)
            CopyDir(template, home);
        return home;
    }

    private static void CopyDir(DirectoryInfo src, DirectoryInfo dst)
    {
        dst.Create();
        foreach (var dir in src.GetDirectories())
            CopyDir(dir, dst.CreateSubdirectory(dir.Name));
        foreach (var file in src.GetFiles())
            file.CopyTo(Path.Combine(dst.FullName, file.Name), overwrite: true);
    }

    // Env for the in-box claude, injected via `docker exec -e`. No API key is present
    // (docker doesn't inherit the host env), so claude takes the subscription path
    // (oracle A1); HOME points at the mounted box home so the JSONL is host-readable.
    private static Dictionary<string, string> BoxEnv(ClaudeHooks hook)
    {
        var env = new Dictionary<string, string>
        {
            ["HOME"] = DockerSandbox.HomeMount,
            [ClaudeHooks.SignalEnvVar] = hook.SignalPathInBox,
        };
        if (hook.PermissionDirInBox is not null)
            env[ClaudeHooks.PermissionEnvVar] = hook.PermissionDirInBox;
        return env;
    }

    private static void TryDeleteDir(DirectoryInfo dir)
    {
        try { dir.Delete(recursive: true); }
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

    // Auto applies the guardrail policy with no human in the loop (allow unless a
    // denylist rule blocks the command). Ask routes to the resolver; an unresolvable
    // call (null) denies — the safe, non-hanging default that replaces acceptEdits'
    // silent mid-turn block.
    private async Task ResolvePermissionAsync(ClaudeHooks hook, PermissionRequest request, CancellationToken ct)
    {
        if (config.Policy == PermissionPolicy.Auto)
        {
            var verdict = autoPolicy.Evaluate(request);
            hook.Respond(request, ClaudePermission.RenderResponse(verdict.Allow, verdict.Reason));
            return;
        }

        var allow = ClaudePermission.IsAllow(await resolver.ResolveAsync(ClaudePermission.ToQuestion(request), DecisionKind.Permission, ct));
        hook.Respond(request, ClaudePermission.RenderResponse(allow, allow ? "approved" : "denied (no approval)"));
    }

    // Fallback only: if the Stop hook never delivered the final text, recover it
    // from the per-session JSONL (oracle A6), polling briefly for the flush lag.
    private static async Task<string> ResolveTextFallbackAsync(string sessionId, string prompt, string projectsRoot, CancellationToken ct)
    {
        for (var waited = 0; waited <= ResolveTimeoutMs; waited += ResolvePollMs)
        {
            var text = ClaudeJsonl.TryReadLastAssistantText(sessionId, prompt, projectsRoot);
            if (!string.IsNullOrWhiteSpace(text)) return text;
            try { await Task.Delay(ResolvePollMs, ct); }
            catch (OperationCanceledException) { break; }
        }
        return "";
    }

    // The host PTY runs a shell that types the `docker exec -it` launch line; the
    // box env (HOME, RA_* hooks, credential) is injected via -e, not here, so the
    // host shell env stays a plain passthrough.
    private static PtyOptions BuildPtyOptions()
    {
        var env = new Dictionary<string, string>();
        foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
            if (kv.Key is string k && kv.Value is string v) env[k] = v;

        return new PtyOptions
        {
            Name = "claude-agent",
            Cols = Cols,
            Rows = Rows,
            Cwd = Environment.CurrentDirectory,
            App = Shell.Executable,
            Environment = env,
        };
    }
}
