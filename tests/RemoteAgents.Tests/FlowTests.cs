using RemoteAgents.Contracts;
using RemoteAgents.Flows;

namespace RemoteAgents.Tests;

public class FlowTests
{
    private sealed class TwoStepFlow : Flow
    {
        protected override async Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct)
        {
            await ctx.RunStep("a", _ => Task.FromResult("ra"), ct);
            await ctx.RunStep("b", _ => Task.FromResult("rb"), ct);
        }
    }

    private sealed class FailingFlow : Flow
    {
        protected override Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct) =>
            ctx.RunStep<string>("boom", _ => throw new InvalidOperationException("nope"), ct);
    }

    // Mirrors production: the context's snapshot label is the config name the registry would hand in.
    private static FlowContext ContextFor(FlowConfig config) =>
        new(config.Name, "proj", "C:/proj", "prompt", []);

    [Fact]
    public async Task ExecuteAsync_runs_all_steps_and_reaches_Completed()
    {
        var config = new FlowConfig("two-step", "test flow");
        var flow = new TwoStepFlow();
        var ctx = ContextFor(config);

        await flow.ExecuteAsync(config, ctx, CancellationToken.None);

        var snap = ctx.Snapshot();
        Assert.Equal(FlowPhase.Completed, snap.Phase);
        Assert.Equal(["a", "b"], snap.Steps.Select(s => s.Name));
        Assert.All(snap.Steps, s => Assert.Equal(StepStatus.Completed, s.Status));
        Assert.Equal("ra", snap.Steps[0].Summary);            // summary = result.ToString()
        Assert.Equal("two-step", snap.Flow);                  // label comes from the flow's config name
    }

    [Fact]
    public async Task Version_is_monotonic_across_the_run()
    {
        var config = new FlowConfig("two-step", "t");
        var flow = new TwoStepFlow();
        var ctx = ContextFor(config);
        var before = ctx.Snapshot().Version;

        await flow.ExecuteAsync(config, ctx, CancellationToken.None);

        Assert.True(ctx.Snapshot().Version > before);
    }

    [Fact]
    public async Task A_failing_step_marks_the_flow_Failed_and_records_the_error()
    {
        var config = new FlowConfig("failing", "t");
        var flow = new FailingFlow();
        var ctx = ContextFor(config);

        await Assert.ThrowsAsync<InvalidOperationException>(() => flow.ExecuteAsync(config, ctx, CancellationToken.None));

        var snap = ctx.Snapshot();
        Assert.Equal(FlowPhase.Failed, snap.Phase);
        Assert.Equal(StepStatus.Failed, snap.Steps[0].Status);
        Assert.Equal("nope", snap.Steps[0].Error);
    }

    [Fact]
    public async Task Changes_replays_latest_for_a_finished_run_then_completes()
    {
        var config = new FlowConfig("two-step", "t");
        var flow = new TwoStepFlow();
        var ctx = ContextFor(config);
        await flow.ExecuteAsync(config, ctx, CancellationToken.None);

        var seen = new List<FlowSnapshot>();
        await foreach (var snap in ctx.Changes(CancellationToken.None))
            seen.Add(snap);

        // A finished run yields exactly one static snapshot, then the stream ends.
        Assert.Single(seen);
        Assert.Equal(FlowPhase.Completed, seen[0].Phase);
    }
}
