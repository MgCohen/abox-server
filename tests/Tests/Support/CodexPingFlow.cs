using RemoteAgents.Domain.Agents;
using RemoteAgents.Domain.Flow;

namespace RemoteAgents.Tests.Support;

// Connectivity flow — drives the real Codex reviewer with the run prompt, one operation. Test fixture
// for the live smoke (needs a real CLI), kept out of the production catalog.
public sealed class CodexPingFlow(IAgentFactory agents) : Flow
{
    protected override Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct) =>
        Run(ctx, agents.Create(Agents.Reviewer, ctx.ProjectDir), new AgentArgs("ping", ctx.Request), ct);
}
