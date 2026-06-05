using RemoteAgents.Flows;

namespace RemoteAgents.Tests;

internal static class Op
{
    public static async Task<TResult> Exec<TArgs, TResult>(
        Flow.Operation<TArgs, TResult> op, TArgs args, string dir = ".")
        where TArgs : OperationArgs
    {
        var flow = new OpFlow<TArgs, TResult>(op, args);
        await flow.ExecuteAsync(new FlowConfig("t", "t"), new FlowContext("t", "t", dir, "test"), CancellationToken.None);
        return flow.Result;
    }
}
