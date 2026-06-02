using RemoteAgents.Contracts;

namespace RemoteAgents.Flows;

public abstract class Flow
{
    private FlowContext _ctx = null!;

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

    protected async Task<T> Run<T>(IOperation<T> operation, CancellationToken ct)
    {
        _ctx.StartOperation(operation.Name);
        Changed?.Invoke();
        try
        {
            var result = await operation.Execute(_ctx, ct).ConfigureAwait(false);
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
    }

    private void SetPhase(FlowPhase phase)
    {
        _ctx.SetPhase(phase);
        Changed?.Invoke();
    }
}
