using RemoteAgents.Events;
using RemoteAgents.Primitives;
using RemoteAgents.Providers.Claude;
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

        return BuildContext(flowName, projectName, projectDir, userPrompt, shouldPush,
            extraSinks: [new ConsoleSink()],
            withBootBanner: true);
    }

    // In-process flavor: the Host has already resolved the project dir and
    // parsed the prompt out of the REST body, and supplies its run-scoped
    // sink (typically a ChannelSink wrapped in RunStateSink). We fold the
    // injected sinks into the composite alongside JsonlSink + the provider
    // JSONL ingest sink — no ConsoleSink, since the Host has no terminal
    // to print to.
    public static async Task<FlowContext> StartInProcessAsync(
        string flowName,
        string projectName,
        string projectDir,
        string userPrompt,
        bool shouldPush,
        IEnumerable<IEventSink> injectedSinks,
        CancellationToken ct = default)
    {
        await SubscriptionGuard.CheckAsync(ct);
        return BuildContext(flowName, projectName, projectDir, userPrompt, shouldPush,
            extraSinks: injectedSinks,
            withBootBanner: false);
    }

    private static FlowContext BuildContext(
        string flowName, string projectName, string projectDir, string userPrompt,
        bool shouldPush, IEnumerable<IEventSink> extraSinks, bool withBootBanner)
    {
        var session = Session.Start(new StartSessionRequest(
            ProjectDir: projectDir,
            ProjectName: projectName,
            UserPrompt: userPrompt,
            FlowName: flowName));

        if (withBootBanner)
            PrintBootBanner(session.Id, flowName, projectName, projectDir, userPrompt, shouldPush);

        var jsonl = new JsonlSink(session.TranscriptFile);
        var sinks = new List<IEventSink>();
        sinks.AddRange(extraSinks);
        sinks.Add(jsonl);
        sinks.Add(new ProviderJsonlIngestSink(session.Dir, projectDir));

        var sink = new CompositeSink([.. sinks]);

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
