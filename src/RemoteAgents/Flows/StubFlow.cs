using RemoteAgents.Steps.Agents;

namespace RemoteAgents.Flows;

// PROVISIONAL walking skeleton — placeholder steps around one minted fake
// agent. Retired at L10.
public sealed class StubFlow(IAgentFactory agents) : Flow
{
    protected override async Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct)
    {
        await Run(new DelayStep("prepare", 800, "ready"), ct);
        await Run(agents.Create("fake", "work", $"process: {ctx.Prompt}"), ct);
        await Run(new DelayStep("finish", 600, "done"), ct);
    }
}
