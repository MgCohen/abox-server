using RemoteAgents.Actors.Agents;

namespace RemoteAgents.Flows;

// PROVISIONAL walking skeleton — placeholder operations around one fake agent.
// Keeps its own fake so it stays CLI-free now that Agents.Implementer is real
// Claude. Retired at L10.
public sealed class StubFlow(IAgentFactory agents) : Flow
{
    private static readonly AgentConfig Placeholder =
        new FakeAgentConfig("implementer", "Placeholder for the walking skeleton.", "fake-model", "You implement.");

    protected override async Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct)
    {
        var agent = agents.Create(Placeholder, ctx.ProjectDir);
        await Run(ctx, new DelayOperation(), new DelayArgs("prepare", 800, "ready"), ct);
        await Run(ctx, agent, new AgentArgs("process", $"process: {ctx.Request}"), ct);
        await Run(ctx, new DelayOperation(), new DelayArgs("finish", 600, "done"), ct);
    }
}
