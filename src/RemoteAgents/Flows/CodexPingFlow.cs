using RemoteAgents.Actors.Agents;

namespace RemoteAgents.Flows;

// PROVISIONAL connectivity flow — drives the real Codex reviewer with the run
// prompt, one operation. Superseded by the L10 recipes.
public sealed class CodexPingFlow(IAgentFactory agents) : Flow
{
    protected override Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct) =>
        Run(agents.Create(Agents.Reviewer).Run(ctx.Request), ct);
}
