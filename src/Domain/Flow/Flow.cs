using System.Collections.Concurrent;
using RemoteAgents.Contracts;
using RemoteAgents.Domain.Flow.Operations;

namespace RemoteAgents.Domain.Flow;

public abstract class Flow
{
    private readonly ConcurrentDictionary<object, byte> _inFlight = new();

    public event Action? Changed;

    protected abstract Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct);

    public async Task ExecuteAsync(FlowConfig config, FlowContext ctx, CancellationToken ct)
    {
        SetPhase(ctx, FlowPhase.Running);
        try
        {
            await RunAsync(config, ctx, ct).ConfigureAwait(false);
            SetPhase(ctx, FlowPhase.Completed);
        }
        catch (OperationCanceledException)
        {
            SetPhase(ctx, FlowPhase.Canceled);
        }
        catch
        {
            SetPhase(ctx, FlowPhase.Failed);
            throw;
        }
    }

    private interface IGate<TArgs, TResult> where TArgs : OperationArgs
    {
        Task<TResult> Execute(TArgs args, CancellationToken ct);
    }

    public abstract class Operation<TArgs, TResult> : IGate<TArgs, TResult>
        where TArgs : OperationArgs
    {
        protected abstract Task<TResult> Invoke(TArgs args, CancellationToken ct);

        Task<TResult> IGate<TArgs, TResult>.Execute(TArgs args, CancellationToken ct) => Invoke(args, ct);
    }

    protected async Task<TResult> Run<TArgs, TResult>(
        FlowContext ctx, Operation<TArgs, TResult> op, TArgs args, CancellationToken ct)
        where TArgs : OperationArgs
    {
        if (!_inFlight.TryAdd(op, 0))
            throw new InvalidOperationException(
                $"Operation '{args.Name}' is already running on this actor; sequence the calls.");
        try
        {
            var record = ctx.StartOperation(args.Name);
            Changed?.Invoke();
            try
            {
                IGate<TArgs, TResult> gate = op;
                var result = await gate.Execute(args, ct).ConfigureAwait(false);
                if (op is IDecisionSource src)
                    foreach (var decision in src.Decisions)
                        ctx.RecordDecision(decision);
                ctx.CompleteOperation(record, result?.ToString());
                Changed?.Invoke();
                return result;
            }
            catch (OperationCanceledException)
            {
                ctx.CancelOperation(record);
                Changed?.Invoke();
                throw;
            }
            catch (Exception ex)
            {
                ctx.FailOperation(record, ex.Message);
                Changed?.Invoke();
                throw;
            }
        }
        finally
        {
            _inFlight.TryRemove(op, out _);
        }
    }

    private void SetPhase(FlowContext ctx, FlowPhase phase)
    {
        ctx.SetPhase(phase);
        Changed?.Invoke();
    }
}
