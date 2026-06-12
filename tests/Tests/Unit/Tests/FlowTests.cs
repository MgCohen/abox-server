using RemoteAgents.Domain.Flow;
using RemoteAgents.Domain.Flow.Operations;
using RemoteAgents.Infrastructure.Operations;

namespace RemoteAgents.Tests.Unit.Tests;

public class FlowTests
{
    private sealed record StepArgs(string Name, string Value) : OperationArgs(Name);

    private sealed class FixedOp : Operation<StepArgs, string>
    {
        protected override Task<string> Invoke(StepArgs args, CancellationToken ct) => Task.FromResult(args.Value);
    }

    private sealed class ThrowingOp : Operation<StepArgs, string>
    {
        protected override Task<string> Invoke(StepArgs args, CancellationToken ct) =>
            throw new InvalidOperationException("nope");
    }

    private sealed class TwoStepFlow : Flow
    {
        protected override async Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct)
        {
            await Run(ctx, new FixedOp(), new StepArgs("a", "ra"), ct);
            await Run(ctx, new FixedOp(), new StepArgs("b", "rb"), ct);
        }
    }

    private sealed class FailingFlow : Flow
    {
        protected override Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct) =>
            Run(ctx, new ThrowingOp(), new StepArgs("boom", ""), ct);
    }

    private sealed class EchoRequestFlow : Flow
    {
        protected override Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct) =>
            Run(ctx, new FixedOp(), new StepArgs("echo", ctx.Request), ct);
    }

    private sealed class ParallelFlow(int count) : Flow
    {
        protected override Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct) =>
            Task.WhenAll(Enumerable.Range(0, count)
                .Select(i => Task.Run(() => Run(ctx, new FixedOp(), new StepArgs($"op{i}", $"v{i}"), ct), ct)));
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
    public async Task A_flow_feeds_run_data_to_an_operation_through_its_args()
    {
        var config = new FlowConfig("echo", "t");
        var flow = new EchoRequestFlow();
        var ctx = ContextFor(config);
        var stream = new SnapshotStream(flow, ctx);

        await flow.ExecuteAsync(config, ctx, CancellationToken.None);

        Assert.Equal("request", stream.Latest.Operations[0].Summary);
    }

    [Fact]
    public async Task Concurrent_operations_are_all_recorded_without_corruption()
    {
        const int count = 64;
        var config = new FlowConfig("parallel", "t");
        var flow = new ParallelFlow(count);
        var ctx = ContextFor(config);
        var stream = new SnapshotStream(flow, ctx);

        await flow.ExecuteAsync(config, ctx, CancellationToken.None);

        var snap = stream.Latest;
        Assert.Equal(FlowPhase.Completed, snap.Phase);
        Assert.Equal(count, snap.Operations.Count);
        Assert.All(snap.Operations, s => Assert.Equal(OperationStatus.Completed, s.Status));
        Assert.Equal(
            Enumerable.Range(0, count).Select(i => $"op{i}").OrderBy(n => n, StringComparer.Ordinal),
            snap.Operations.Select(s => s.Name).OrderBy(n => n, StringComparer.Ordinal));
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

    [Fact]
    public void FlowDefinition_rejects_a_non_Flow_type() =>
        Assert.Throws<ArgumentException>(() =>
            new FlowDefinition(typeof(string), new FlowConfig("x", "y")));

    [Fact]
    public void FlowDefinition_accepts_a_concrete_flow_type() =>
        Assert.Equal(typeof(TwoStepFlow),
            new FlowDefinition(typeof(TwoStepFlow), new FlowConfig("x", "y")).FlowType);
}
