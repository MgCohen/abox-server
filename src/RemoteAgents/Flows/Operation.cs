namespace RemoteAgents.Flows;

public abstract class Operation<TArgs, TResult> : Flow.Operation<TArgs, TResult>
    where TArgs : OperationArgs
{
}
