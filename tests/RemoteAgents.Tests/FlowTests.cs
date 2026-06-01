using RemoteAgents.Contracts;
using RemoteAgents.Flows;

namespace RemoteAgents.Tests;

public class FlowTests
{
    private sealed class TwoStepFlow : Flow
    {
        public TwoStepFlow() => Configure(new FlowConfig("two-step", "test flow"));
        protected override async Task RunAsync(CancellationToken ct)
        {
            await RunStep("a", _ => Task.FromResult("ra"), ct);
            await RunStep("b", _ => Task.FromResult("rb"), ct);
        }
    }

    private sealed class FailingFlow : Flow
    {
        public FailingFlow() => Configure(new FlowConfig("failing", "test flow"));
        protected override Task RunAsync(CancellationToken ct) =>
            RunStep<string>("boom", _ => throw new InvalidOperationException("nope"), ct);
    }

    [Fact]
    public async Task ExecuteAsync_runs_all_steps_and_reaches_Completed()
    {
        var flow = new TwoStepFlow();
        flow.Initialize("proj", "C:/proj", "prompt", []);

        await flow.ExecuteAsync(CancellationToken.None);

        var snap = flow.Snapshot();
        Assert.Equal(FlowPhase.Completed, snap.Phase);
        Assert.Equal(["a", "b"], snap.Steps.Select(s => s.Name));
        Assert.All(snap.Steps, s => Assert.Equal(StepStatus.Completed, s.Status));
        Assert.Equal("ra", snap.Steps[0].Summary);            // summary = result.ToString()
    }

    [Fact]
    public async Task Version_is_monotonic_across_the_run()
    {
        var flow = new TwoStepFlow();
        flow.Initialize("p", "d", "x", []);
        var before = flow.Snapshot().Version;

        await flow.ExecuteAsync(CancellationToken.None);

        Assert.True(flow.Snapshot().Version > before);
    }

    [Fact]
    public async Task A_failing_step_marks_the_flow_Failed_and_records_the_error()
    {
        var flow = new FailingFlow();
        flow.Initialize("p", "d", "x", []);

        await Assert.ThrowsAsync<InvalidOperationException>(() => flow.ExecuteAsync(CancellationToken.None));

        var snap = flow.Snapshot();
        Assert.Equal(FlowPhase.Failed, snap.Phase);
        Assert.Equal(StepStatus.Failed, snap.Steps[0].Status);
        Assert.Equal("nope", snap.Steps[0].Error);
    }

    [Fact]
    public async Task Changes_replays_latest_for_a_finished_run_then_completes()
    {
        var flow = new TwoStepFlow();
        flow.Initialize("p", "d", "x", []);
        await flow.ExecuteAsync(CancellationToken.None);

        var seen = new List<FlowSnapshot>();
        await foreach (var snap in flow.Changes(CancellationToken.None))
            seen.Add(snap);

        // A finished run yields exactly one static snapshot, then the stream ends.
        Assert.Single(seen);
        Assert.Equal(FlowPhase.Completed, seen[0].Phase);
    }
}
