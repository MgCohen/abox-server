namespace RemoteAgents.Flows;

// PROVISIONAL stub operation — retired with StubFlow at L10.
public sealed class DelayOperation(string name, int delayMs, string result) : IOperation<string>
{
    public string Name => name;

    public async Task<string> Execute(FlowContext ctx, CancellationToken ct)
    {
        await Task.Delay(delayMs, ct).ConfigureAwait(false);
        return result;
    }
}
