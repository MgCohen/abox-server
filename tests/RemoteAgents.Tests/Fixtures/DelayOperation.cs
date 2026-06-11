using RemoteAgents.Infrastructure.Operations;

namespace RemoteAgents.Tests;

// PROVISIONAL stub operation — a placeholder step, kept as a test fixture for the skeleton flows.
public sealed class DelayOperation : Operation<DelayArgs, string>
{
    protected override async Task<string> Invoke(DelayArgs args, CancellationToken ct)
    {
        await Task.Delay(args.DelayMs, ct).ConfigureAwait(false);
        return args.Result;
    }
}
