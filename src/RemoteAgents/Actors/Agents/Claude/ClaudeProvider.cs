using Porta.Pty;
using RemoteAgents.Tools.CommandLine;

namespace RemoteAgents.Actors.Agents.Claude;

public sealed class ClaudeProvider(ClaudeConfig config) : IProvider
{
    private const int Cols = 120;
    private const int Rows = 40;
    private const int LaunchSettleIdleMs = 1_000;
    private const int LaunchSettleMinWaitMs = 3_500;   // oracle A4: covers the cmd→claude silent gap
    private const int LaunchSettleCapMs = 8_000;
    private const int DialogSettleCapMs = 5_000;
    private const int SubmitSettleMs = 500;            // oracle A5: anti-paste pause
    private const int ResponseIdleMs = 6_000;
    private const int ResponseCapMs = 5 * 60_000;
    private const int ExitSettleIdleMs = 500;
    private const int ExitSettleCapMs = 5_000;
    private const int WaitForExitMs = 15_000;
    private const int ReaderDrainMs = 2_000;
    private const int ResolveTimeoutMs = 2_000;
    private const int ResolvePollMs = 100;
    private const int MaxOverallMs = 10 * 60_000;      // oracle A10: wall-clock cap → guaranteed teardown

    public async Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct)
    {
        var isResume = request.SessionId is not null;
        var sessionId = request.SessionId ?? Guid.NewGuid().ToString();
        var args = ClaudeProtocol.BuildArgs(sessionId, isResume, config.PermissionMode, config.Model, config.SystemPrompt);
        var launchLine = "claude " + string.Join(' ', args.Select(Shell.QuoteArg));

        using var deadlineCts = new CancellationTokenSource(MaxOverallMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, deadlineCts.Token);
        var dct = linkedCts.Token;

        var pty = await PtyProvider.SpawnAsync(BuildPtyOptions(request.ProjectDir), dct);
        await using var session = new PtySession(pty, onChunk: null, dct);

        await session.WriteLineAsync(launchLine, dct);
        await session.WaitIdleAsync(LaunchSettleIdleMs, LaunchSettleCapMs, minWaitMs: LaunchSettleMinWaitMs, ct: dct);

        await MaybeDismissDialogAsync(session, dct);

        await session.SubmitAsync(request.Prompt, SubmitSettleMs, dct);
        await session.WaitIdleAsync(ResponseIdleMs, ResponseCapMs, ct: dct);

        await session.WriteLineAsync("/exit", dct);
        await session.WaitIdleAsync(ExitSettleIdleMs, ExitSettleCapMs, ct: dct);
        await session.WriteLineAsync("exit", dct);
        var exitCode = await session.ShutdownAsync(WaitForExitMs, ReaderDrainMs);

        var (text, transcript) = await ResolveOutputAsync(request.ProjectDir, sessionId, request.Prompt, session, dct);
        return new DriveResult(text, sessionId, exitCode, session.Buffer, transcript);
    }

    private async Task MaybeDismissDialogAsync(PtySession session, CancellationToken ct)
    {
        var keys = ClaudeProtocol.DetectStartupDialog(session.Buffer) switch
        {
            StartupDialog.Trust => "\r",
            StartupDialog.BypassWarning => "2\r",
            _ => null,
        };
        if (keys is null) return;

        await session.WriteAsync(keys, ct);
        await session.WaitIdleAsync(LaunchSettleIdleMs, DialogSettleCapMs, ct: ct);
    }

    // Oracle A6: the per-session JSONL is the authoritative text source; poll
    // briefly for Claude's final-flush lag, then fall back to the buffer scrape.
    private static async Task<(string Text, IReadOnlyList<AgentTurn> Transcript)> ResolveOutputAsync(
        string projectDir, string sessionId, string prompt, PtySession session, CancellationToken ct)
    {
        string? jsonl = null;
        for (var waited = 0; waited <= ResolveTimeoutMs; waited += ResolvePollMs)
        {
            jsonl = ClaudeJsonl.TryReadLastAssistantText(projectDir, sessionId, prompt);
            if (!string.IsNullOrWhiteSpace(jsonl)) break;
            try { await Task.Delay(ResolvePollMs, ct); }
            catch (OperationCanceledException) { break; }
        }

        var text = !string.IsNullOrWhiteSpace(jsonl)
            ? jsonl!
            : ClaudeProtocol.ExtractAssistantText(session.Buffer, prompt);
        var transcript = ClaudeJsonl.TryReadLastTurnTranscript(projectDir, sessionId, prompt) ?? [];
        return (text, transcript);
    }

    private static PtyOptions BuildPtyOptions(string projectDir)
    {
        var env = new Dictionary<string, string>();
        foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
            if (kv.Key is string k && kv.Value is string v) env[k] = v;
        foreach (var key in EnvScrub.SubscriptionKeys) env[key] = "";

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
}
