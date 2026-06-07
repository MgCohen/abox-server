using RemoteAgents.Actors.Agents;

namespace RemoteAgents.Flows;

// PROVISIONAL connectivity flow — drives the real Claude implementer with the
// run prompt, one operation. Superseded by the L10 recipes.
public sealed class ClaudePingFlow(IAgentFactory agents) : Flow
{
    protected override Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct) =>
        Run(ctx, agents.Create(Agents.Implementer, ctx.ProjectDir), new AgentArgs("ping", ctx.Request), ct);
}
