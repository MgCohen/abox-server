using System.Collections.Concurrent;
using RemoteAgents.Contracts;

namespace RemoteAgents.Flows;

public abstract class Flow
{
    private FlowContext _ctx = null!;
    private readonly ConcurrentDictionary<object, byte> _inFlight = new();

    public event Action? Changed;

    protected abstract Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct);

    public async Task ExecuteAsync(FlowConfig config, FlowContext ctx, CancellationToken ct)
    {
        _ctx = ctx;
        SetPhase(FlowPhase.Running);
        try
        {
            await RunAsync(config, ctx, ct).ConfigureAwait(false);
            SetPhase(FlowPhase.Completed);
        }
        catch (OperationCanceledException)
        {
            SetPhase(FlowPhase.Canceled);
        }
        catch
        {
            SetPhase(FlowPhase.Failed);
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
        Operation<TArgs, TResult> op, TArgs args, CancellationToken ct)
        where TArgs : OperationArgs
    {
        if (!_inFlight.TryAdd(op, 0))
            throw new InvalidOperationException(
                $"Operation '{args.Name}' is already running on this actor; sequence the calls.");

        _ctx.StartOperation(args.Name);
        Changed?.Invoke();
        try
        {
            IGate<TArgs, TResult> gate = op;
            var result = await gate.Execute(args, ct).ConfigureAwait(false);
            _ctx.CompleteOperation(result?.ToString());
            Changed?.Invoke();
            return result;
        }
        catch (Exception ex)
        {
            _ctx.FailOperation(ex.Message);
            Changed?.Invoke();
            throw;
        }
        finally
        {
            _inFlight.TryRemove(op, out _);
        }
    }

    private void SetPhase(FlowPhase phase)
    {
        _ctx.SetPhase(phase);
        Changed?.Invoke();
    }
}
