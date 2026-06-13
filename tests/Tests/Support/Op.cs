using ABox.Domain.Flow;
using ABox.Infrastructure.Operations;

namespace ABox.Tests.Support;

internal static class Op
{
    public static async Task<TResult> Exec<TArgs, TResult>(
        Operation<TArgs, TResult> op, TArgs args, string dir = ".", CancellationToken ct = default)
        where TArgs : OperationArgs
    {
        var flow = new OpFlow<TArgs, TResult>(op, args);
        await flow.ExecuteAsync(new FlowConfig("t", "t"), new FlowContext("t", "t", dir, "test"), ct);
        return flow.Result;
    }
}
