using RemoteAgents.Flows;
using RemoteAgents.Sessions;

namespace RemoteAgents.Hosting;

// Bridge: wraps a pre-refactor IFlow definition (ClaudeOnlyFlow, ReviewFlow, …)
// as a new-style Flow aggregate so it executes under the unified lifecycle.
//
// One Step per IFlow. The IFlow's internal "phases" (claude work, validate,
// review, commit) are NOT surfaced as separate steps here — that detail is
// only available once the flow is ported native (step 7 of the refactor).
// Agent-level events (StreamChunk, ToolUse, …) are dropped at this boundary;
// per D3, the UI sees state changes per completion boundary, not per token.
public sealed class LegacyFlowAdapter : Flow
{
    private readonly IFlow    _flow;
    private readonly string   _project;
    private readonly string   _projectDir;
    private readonly string   _prompt;
    private readonly string[] _args;

    public LegacyFlowAdapter(IFlow flow, string project, string projectDir, string prompt, string[] args)
    {
        _flow       = flow;
        _project    = project;
        _projectDir = projectDir;
        _prompt     = prompt;
        _args       = args;
    }

    public override string Name => _flow.Name;

    protected override Task ExecuteAsync(CancellationToken ct) =>
        Step(_flow.Name, async () =>
        {
            await using var fctx = await FlowBootstrap.StartInProcessAsync(
                flowName:      _flow.Name,
                projectName:   _project,
                projectDir:    _projectDir,
                userPrompt:    _prompt,
                shouldPush:    _args.Contains("--push"),
                injectedSinks: [],     // agent events discarded at this boundary
                ct:            ct);

            var fargs  = new FlowArgs(_project, _prompt, _args, fctx.ShouldPush);
            var result = await new FlowRunner().RunAsync(_flow, fctx, fargs, ct);

            // Gate failures (validation/verdict/dirty-tree) and unhandled
            // errors are non-zero exit codes in the CLI path. Map both to
            // a thrown failure so the new Flow surfaces Phase=Failed in its
            // snapshot.
            if (FlowRunner.MapToExitCode(result.Reason) != 0)
                throw new InvalidOperationException(
                    result.Detail is null
                        ? $"Flow ended with {result.Reason}."
                        : $"Flow ended with {result.Reason}: {result.Detail}");
        });
}
