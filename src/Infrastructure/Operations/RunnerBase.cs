namespace RemoteAgents.Infrastructure.Operations;

public abstract class RunnerBase
{
    protected Task<TResult> Execute<TArgs, TResult>(Operation<TArgs, TResult> op, TArgs args, CancellationToken ct)
        where TArgs : OperationArgs
    {
        IGate<TArgs, TResult> gate = op;
        return gate.Execute(args, ct);
    }
}
