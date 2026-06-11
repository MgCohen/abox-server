using RemoteAgents.Domain.Agents;
using RemoteAgents.Domain.Flow;

namespace RemoteAgents.Tests;

// Connectivity flow — drives the real Claude implementer with the run prompt, one operation. Test
// fixture for the live smoke (needs a real CLI), kept out of the production catalog.
public sealed class ClaudePingFlow(IAgentFactory agents) : Flow
{
    protected override Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct) =>
        Run(ctx, agents.Create(Agents.Implementer, ctx.ProjectDir), new AgentArgs("ping", ctx.Request), ct);
}
