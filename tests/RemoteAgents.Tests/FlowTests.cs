using RemoteAgents.Contracts;
using RemoteAgents.Flows;

namespace RemoteAgents.Tests;

public class FlowTests
{
    private sealed class FixedStep(string name, string result) : IOperation<string>
    {
        public string Name => name;
        public Task<string> Execute(FlowContext ctx, CancellationToken ct) => Task.FromResult(result);
    }

    private sealed class ThrowingStep(string name) : IOperation<string>
    {
        public string Name => name;
        public Task<string> Execute(FlowContext ctx, CancellationToken ct) =>
            throw new InvalidOperationException("nope");
    }

    private sealed class RequestStep(string name) : IOperation<string>
    {
        public string Name => name;
        public Task<string> Execute(FlowContext ctx, CancellationToken ct) => Task.FromResult(ctx.Request);
    }

    private sealed class TwoStepFlow : Flow
    {
        protected override async Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct)
        {
            await Run(new FixedStep("a", "ra"), ct);
            await Run(new FixedStep("b", "rb"), ct);
        }
    }

    private sealed class FailingFlow : Flow
    {
        protected override Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct) =>
            Run(new ThrowingStep("boom"), ct);
    }

    private sealed class RequestFlow : Flow
    {
        protected override Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct) =>
            Run(new RequestStep("echo"), ct);
    }

    private static FlowContext ContextFor(FlowConfig config) =>
        new(config.Name, "proj", "C:/proj", "request");

    [Fact]
    public async Task ExecuteAsync_runs_all_operations_and_reaches_Completed()
    {
        var config = new FlowConfig("two-step", "test flow");
        var flow = new TwoStepFlow();
        var ctx = ContextFor(config);
        var stream = new SnapshotStream(flow, ctx);

        await flow.ExecuteAsync(config, ctx, CancellationToken.None);

        var snap = stream.Latest;
        Assert.Equal(FlowPhase.Completed, snap.Phase);
        Assert.Equal(["a", "b"], snap.Operations.Select(s => s.Name));
        Assert.All(snap.Operations, s => Assert.Equal(OperationStatus.Completed, s.Status));
        Assert.Equal("ra", snap.Operations[0].Summary);
        Assert.Equal("two-step", snap.Flow);
    }

    [Fact]
    public async Task Version_is_monotonic_across_the_run()
    {
        var config = new FlowConfig("two-step", "t");
        var flow = new TwoStepFlow();
        var ctx = ContextFor(config);
        var stream = new SnapshotStream(flow, ctx);
        var before = stream.Latest.Version;

        await flow.ExecuteAsync(config, ctx, CancellationToken.None);

        Assert.True(stream.Latest.Version > before);
    }

    [Fact]
    public async Task A_failing_operation_marks_the_flow_Failed_and_records_the_error()
    {
        var config = new FlowConfig("failing", "t");
        var flow = new FailingFlow();
        var ctx = ContextFor(config);
        var stream = new SnapshotStream(flow, ctx);

        await Assert.ThrowsAsync<InvalidOperationException>(() => flow.ExecuteAsync(config, ctx, CancellationToken.None));

        var snap = stream.Latest;
        Assert.Equal(FlowPhase.Failed, snap.Phase);
        Assert.Equal(OperationStatus.Failed, snap.Operations[0].Status);
        Assert.Equal("nope", snap.Operations[0].Error);
    }

    [Fact]
    public async Task An_operation_reads_run_data_from_the_context_it_is_handed()
    {
        var config = new FlowConfig("request", "t");
        var flow = new RequestFlow();
        var ctx = ContextFor(config);
        var stream = new SnapshotStream(flow, ctx);

        await flow.ExecuteAsync(config, ctx, CancellationToken.None);

        Assert.Equal("request", stream.Latest.Operations[0].Summary);
    }

    [Fact]
    public async Task Changes_replays_latest_for_a_finished_run_then_completes()
    {
        var config = new FlowConfig("two-step", "t");
        var flow = new TwoStepFlow();
        var ctx = ContextFor(config);
        var stream = new SnapshotStream(flow, ctx);
        await flow.ExecuteAsync(config, ctx, CancellationToken.None);

        var seen = new List<FlowSnapshot>();
        await foreach (var snap in stream.Changes(CancellationToken.None))
            seen.Add(snap);

        Assert.Single(seen);
        Assert.Equal(FlowPhase.Completed, seen[0].Phase);
    }
}
