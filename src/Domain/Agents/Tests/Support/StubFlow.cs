using ABox.Domain.Agents;
using ABox.Domain.Flow;
using ABox.Domain.Flow.Operations;

namespace ABox.Agents.Tests.Support;

// Walking-skeleton stub: placeholder steps around one fake agent, kept CLI-free. Test fixture used to
// exercise the live host end-to-end (start → run → snapshot → cancel) without a real provider. Builds
// the fake agent directly — the production factory only mints real Claude/Codex providers.
public sealed class StubFlow : Flow
{
    private static readonly FakeAgentConfig Placeholder =
        new("implementer", "Placeholder for the walking skeleton.", "fake-model", "You implement.", Reply: "[implementer] done");

    protected override async Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct)
    {
        var agent = new Agent(new FakeProvider(Placeholder), new NonInteractiveResolver(), null, ctx.ProjectDir);
        await Run(ctx, new DelayOperation(), new DelayArgs("prepare", 800, "ready"), ct);
        await Run(ctx, agent, new AgentArgs("process", $"process: {ctx.Request}"), ct);
        await Run(ctx, new DelayOperation(), new DelayArgs("finish", 600, "done"), ct);
    }
}
