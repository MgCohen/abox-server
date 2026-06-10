using RemoteAgents.Domain.Agents;
using RemoteAgents.Domain.Flow;

namespace RemoteAgents.Features.Flows.Definitions;

// PROVISIONAL connectivity flow — drives the real Codex reviewer with the run
// prompt, one operation. Superseded by the L10 recipes.
public sealed class CodexPingFlow(IAgentFactory agents) : Flow
{
    protected override Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct) =>
        Run(ctx, agents.Create(Agents.Reviewer, ctx.ProjectDir), new AgentArgs("ping", ctx.Request), ct);
}
