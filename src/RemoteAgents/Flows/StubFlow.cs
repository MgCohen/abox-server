namespace RemoteAgents.Flows;

/// <summary>
/// L2 walking-skeleton flow: a few placeholder steps with no real work, used to
/// prove the snapshot pipe end-to-end before agents and real steps exist. Retired
/// once real recipes land (L10).
/// </summary>
public sealed class StubFlow : Flow
{
    protected override async Task RunAsync(CancellationToken ct)
    {
        await RunStep("prepare", async c => { await Task.Delay(800, c); return "ready"; }, ct);
        await RunStep("work", async c => { await Task.Delay(1200, c); return $"processed: {Prompt}"; }, ct);
        await RunStep("finish", async c => { await Task.Delay(600, c); return "done"; }, ct);
    }
}
