namespace RemoteAgents.Flows;

// PROVISIONAL walking skeleton — placeholder steps, no real work. Retired at L10.
public sealed class StubFlow : Flow
{
    protected override async Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct)
    {
        await Run(new DelayStep("prepare", 800, "ready"), ct);
        await Run(new ProcessStep("work", 1200), ct);
        await Run(new DelayStep("finish", 600, "done"), ct);
    }

    private sealed class ProcessStep(string name, int delayMs) : IStepHandler<string>
    {
        public string Name => name;

        public async Task<string> RunAsync(FlowContext ctx, CancellationToken ct)
        {
            await Task.Delay(delayMs, ct).ConfigureAwait(false);
            return $"processed: {ctx.Prompt}";
        }
    }
}
