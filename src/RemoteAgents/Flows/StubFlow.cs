namespace RemoteAgents.Flows;

/// <summary>
/// L2 walking-skeleton flow: a few placeholder steps with no real work, used to
/// prove the snapshot pipe end-to-end before agents and real steps exist. Retired
/// once real recipes land (L10).
/// </summary>
public sealed class StubFlow(FlowConfig config) : Flow(config)
{
    protected override async Task RunAsync(FlowContext ctx, CancellationToken ct)
    {
        await ctx.RunStep("prepare", async c => { await Task.Delay(800, c); return "ready"; }, ct);
        await ctx.RunStep("work", async c => { await Task.Delay(1200, c); return $"processed: {ctx.Prompt}"; }, ct);
        await ctx.RunStep("finish", async c => { await Task.Delay(600, c); return "done"; }, ct);
    }
}
