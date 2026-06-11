using RemoteAgents.Domain.Flow;

namespace RemoteAgents.Domain.Flow.Operations;

public abstract class Operation<TArgs, TResult> : Flow.Operation<TArgs, TResult>
    where TArgs : OperationArgs
{
}
