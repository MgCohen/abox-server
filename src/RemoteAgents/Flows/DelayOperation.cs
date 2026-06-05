namespace RemoteAgents.Flows;

// PROVISIONAL stub operation — retired with StubFlow at L10.
public sealed class DelayOperation : Flow.Operation<DelayArgs, string>
{
    protected override async Task<string> Invoke(DelayArgs args, CancellationToken ct)
    {
        await Task.Delay(args.DelayMs, ct).ConfigureAwait(false);
        return args.Result;
    }
}
