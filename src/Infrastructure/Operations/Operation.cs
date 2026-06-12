namespace ABox.Infrastructure.Operations;

public abstract class Operation<TArgs, TResult> : IGate<TArgs, TResult>
    where TArgs : OperationArgs
{
    protected abstract Task<TResult> Invoke(TArgs args, CancellationToken ct);

    Task<TResult> IGate<TArgs, TResult>.Execute(TArgs args, CancellationToken ct) => Invoke(args, ct);
}
