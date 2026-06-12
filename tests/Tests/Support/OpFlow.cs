using ABox.Domain.Flow;
using ABox.Infrastructure.Operations;

namespace ABox.Tests.Support;

internal sealed class OpFlow<TArgs, TResult>(Operation<TArgs, TResult> op, TArgs args) : Flow
    where TArgs : OperationArgs
{
    public TResult Result { get; private set; } = default!;

    protected override async Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct) =>
        Result = await Run(ctx, op, args, ct);
}
