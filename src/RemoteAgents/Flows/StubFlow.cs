using RemoteAgents.Actors.Agents;

namespace RemoteAgents.Flows;

// PROVISIONAL walking skeleton — placeholder operations around one minted fake
// agent. Retired at L10.
public sealed class StubFlow(IAgentFactory agents) : Flow
{
    protected override async Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct)
    {
        var agent = agents.Create(Agents.Implementer);
        await Run(new DelayOperation("prepare", 800, "ready"), ct);
        await Run(agent.Run($"process: {ctx.Request}"), ct);
        await Run(new DelayOperation("finish", 600, "done"), ct);
    }
}
