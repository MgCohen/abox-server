using RemoteAgents.Engine.Flows;

namespace RemoteAgents.Engine.Operations;

public abstract class Operation<TArgs, TResult> : Flow.Operation<TArgs, TResult>
    where TArgs : OperationArgs
{
}
