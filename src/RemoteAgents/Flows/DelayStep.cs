namespace RemoteAgents.Flows;

// PROVISIONAL stub handler — retired with StubFlow at L10.
public sealed class DelayStep(string name, int delayMs, string result) : IStepHandler<string>
{
    public string Name => name;

    public async Task<string> RunAsync(FlowContext ctx, CancellationToken ct)
    {
        await Task.Delay(delayMs, ct).ConfigureAwait(false);
        return result;
    }
}
