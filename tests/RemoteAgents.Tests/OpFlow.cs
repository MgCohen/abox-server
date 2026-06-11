using RemoteAgents.Domain.Flow;
using RemoteAgents.Domain.Flow.Operations;

namespace RemoteAgents.Tests;

internal sealed class OpFlow<TArgs, TResult>(Flow.Operation<TArgs, TResult> op, TArgs args) : Flow
    where TArgs : OperationArgs
{
    public TResult Result { get; private set; } = default!;

    protected override async Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct) =>
        Result = await Run(ctx, op, args, ct);
}
