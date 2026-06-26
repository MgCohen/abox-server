using ABox.Domain.Agents;
using ABox.Domain.Flow;

namespace ABox.Agents.Tests.Support;

// Connectivity flow — drives the real Claude implementer with the run prompt, one operation. Test
// fixture for the live smoke (needs a real CLI), kept out of the production catalog.
public sealed class ClaudePingFlow(IAgentFactory agents) : Flow
{
    protected override async Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct)
    {
        await using var agent = agents.Create(DomainAgents.Implementer, ctx.ProjectDir);
        await Run(ctx, agent, new AgentArgs("ping", ctx.Request), ct);
    }
}
