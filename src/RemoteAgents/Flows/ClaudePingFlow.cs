using RemoteAgents.Actors.Agents;
using RemoteAgents.Actors.Agents.Claude;
using RemoteAgents.Tools.CommandLine;

namespace RemoteAgents.Flows;

// PROVISIONAL connectivity flow — guards subscription billing, then drives the
// real Claude implementer with the run prompt, one operation. Superseded by the
// L10 recipes.
public sealed class ClaudePingFlow(IAgentFactory agents) : Flow
{
    protected override async Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct)
    {
        await SubscriptionGuard.CheckAsync(EnvScrub.SubscriptionKeys, "claude", ct);
        await Run(agents.Create(Agents.Implementer, ctx.ProjectDir), new AgentArgs("ping", ctx.Request), ct);
    }
}
