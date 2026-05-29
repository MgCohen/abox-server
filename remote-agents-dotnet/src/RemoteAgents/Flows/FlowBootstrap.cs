using RemoteAgents.Events;
using RemoteAgents.Primitives;
using RemoteAgents.Sessions;

namespace RemoteAgents.Flows;

// Per-flow context. Owns the session, the composed sink (console + jsonl +
// provider-ingest), and the parsed args. IAsyncDisposable so the JsonlSink
// flushes on exit.
public sealed class FlowContext : IAsyncDisposable
{
    public required string FlowName { get; init; }
    public required string ProjectName { get; init; }
    public required string ProjectDir { get; init; }
    public required string UserPrompt { get; init; }
    public required bool ShouldPush { get; init; }
    public required Session Session { get; init; }
    public required IEventSink Sink { get; init; }

    internal JsonlSink? OwnedJsonl { get; init; }

    // Check the working tree and abort the flow if it's dirty. Returns
    // false (and ends the session + sets ExitCode) if the caller should
    // bail out.
    public async Task<bool> EnsureCleanTreeAsync(CancellationToken ct = default)
    {
        if (!await GitOps.IsDirtyAsync(ProjectDir, ct)) return true;
        await Sink.PhaseFailAsync("abort", "working tree is dirty. Commit or stash first.", ct);
        Session.End(SessionResult.AbortedDirtyTree);
        Environment.ExitCode = 2;
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (OwnedJsonl is not null) await OwnedJsonl.DisposeAsync();
    }
}

// Shared flow prelude: arg parsing, subscription guard, project resolve,
// session.Start, sink composition. Both review flows reduce to ~15 lines
// after using this.
public static class FlowBootstrap
{
    // Parses `<project> "<prompt>" [--push]`. On bad args, prints usage to
    // stderr, sets Environment.ExitCode=2, and returns null — the flow
    // should `return` immediately.
    public static async Task<FlowContext?> StartAsync(
        string[] args,
        string flowName,
        CancellationToken ct = default)
    {
        var argv = args.ToList();
        var pushIdx = argv.IndexOf("--push");
        var shouldPush = pushIdx >= 0;
        if (shouldPush) argv.RemoveAt(pushIdx);

        if (argv.Count < 2)
        {
            Console.Error.WriteLine($"Usage: dotnet run flows/{flowName}.cs <project> \"<prompt>\" [--push]");
            Environment.ExitCode = 2;
            return null;
        }

        var projectName = argv[0];
        var userPrompt = string.Join(' ', argv.Skip(1)).Trim();

        await SubscriptionGuard.CheckAsync(ct);

        var projectDir = ProjectRegistry.Resolve(projectName);
        var session = Session.Start(new StartSessionRequest(
            ProjectDir: projectDir,
            ProjectName: projectName,
            UserPrompt: userPrompt,
            FlowName: flowName));

        PrintBootBanner(session.Id, flowName, projectName, projectDir, userPrompt, shouldPush);

        var jsonl = new JsonlSink(session.TranscriptFile);
        var sink = new CompositeSink(
            new ConsoleSink(),
            jsonl,
            new ProviderJsonlIngestSink(session.Dir, projectDir));

        return new FlowContext
        {
            FlowName = flowName,
            ProjectName = projectName,
            ProjectDir = projectDir,
            UserPrompt = userPrompt,
            ShouldPush = shouldPush,
            Session = session,
            Sink = sink,
            OwnedJsonl = jsonl,
        };
    }

    private static void PrintBootBanner(
        string sessionId, string flowName, string projectName, string projectDir,
        string userPrompt, bool shouldPush)
    {
        Console.WriteLine($"[{sessionId}]");
        Console.WriteLine($"  flow:    {flowName}");
        Console.WriteLine($"  project: {projectName} ({projectDir})");
        Console.WriteLine($"  prompt:  {userPrompt}");
        Console.WriteLine($"  push:    {(shouldPush ? "yes" : "no")}");
        Console.WriteLine();
    }
}
