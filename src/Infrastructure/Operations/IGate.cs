namespace ABox.Infrastructure.Operations;

internal interface IGate<TArgs, TResult> where TArgs : OperationArgs
{
    Task<TResult> Execute(TArgs args, CancellationToken ct);
}
