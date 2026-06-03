using Porta.Pty;
using RemoteAgents.Tools.CommandLine;

namespace RemoteAgents.Actors.Agents.Claude;

public sealed class ClaudeProvider(ClaudeConfig config) : IProvider
{
    private const int Cols = 120;
    private const int Rows = 40;
    private const int PollMs = 150;
    private const int StartupCapMs = 30_000;           // cold start + plugins + remote-control attach
    private const int ReadySettleMs = 1_200;           // input bar must hold quiet this long before we type
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
        await DismissStartupDialogsAsync(session, dct);

        // Positive readiness: the input bar is present AND has settled. A
        // mid-startup re-render (e.g. remote-control attaching) is byte-for-byte
        // identical to a settled idle, so an idle-only wait drops the prompt —
        // wait for the marker, not for silence.
        if (!await session.WaitUntilAsync(ClaudeProtocol.IsPromptReady, ReadySettleMs, StartupCapMs, PollMs, dct))
            throw new InvalidOperationException(
                $"Claude input bar did not become ready within {StartupCapMs} ms; cannot submit the prompt.");

        await session.SubmitAsync(request.Prompt, SubmitSettleMs, dct);
        await session.WaitIdleAsync(ResponseIdleMs, ResponseCapMs, ct: dct);

        await session.WriteLineAsync("/exit", dct);
        await session.WaitIdleAsync(ExitSettleIdleMs, ExitSettleCapMs, ct: dct);
        await session.WriteLineAsync("exit", dct);
        var exitCode = await session.ShutdownAsync(WaitForExitMs, ReaderDrainMs);

        var (text, transcript) = await ResolveOutputAsync(sessionId, request.Prompt, session, dct);
        return new DriveResult(text, sessionId, exitCode, session.Buffer, transcript);
    }

    // Startup dialogs (trust / bypass) precede the input bar. Dismiss each kind
    // at most once — sending its keystroke twice would land in the next screen
    // and corrupt the prompt — and stop as soon as the bar is ready.
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

    // Oracle A6: the per-session JSONL is the authoritative text source; poll
    // briefly for Claude's final-flush lag, then fall back to the buffer scrape.
    private static async Task<(string Text, IReadOnlyList<AgentTurn> Transcript)> ResolveOutputAsync(
        string sessionId, string prompt, PtySession session, CancellationToken ct)
    {
        string? jsonl = null;
        for (var waited = 0; waited <= ResolveTimeoutMs; waited += ResolvePollMs)
        {
            jsonl = ClaudeJsonl.TryReadLastAssistantText(sessionId, prompt);
            if (!string.IsNullOrWhiteSpace(jsonl)) break;
            try { await Task.Delay(ResolvePollMs, ct); }
            catch (OperationCanceledException) { break; }
        }

        var text = !string.IsNullOrWhiteSpace(jsonl)
            ? jsonl!
            : ClaudeProtocol.ExtractAssistantText(session.Buffer, prompt);
        var transcript = ClaudeJsonl.TryReadLastTurnTranscript(sessionId, prompt) ?? [];
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
