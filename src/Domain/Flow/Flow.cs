using System.Collections.Concurrent;
using RemoteAgents.Domain.Flow.Operations;
using RemoteAgents.Infrastructure.Operations;

namespace RemoteAgents.Domain.Flow;

public abstract class Flow : RunnerBase
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
                var result = await Execute(op, args, ct).ConfigureAwait(false);
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
