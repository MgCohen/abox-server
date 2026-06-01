using RemoteAgents.Contracts;

namespace RemoteAgents.Flows;

/// <summary>
/// A flow recipe and its orchestration. Authors override <see cref="RunAsync"/> to
/// compose steps via <see cref="RunStep"/>; <see cref="ExecuteAsync"/> drives the
/// lifecycle (Pending → Running → terminal). The flow holds no snapshot, version, or
/// lock — it writes the run's data through the <see cref="FlowContext"/> it's handed and
/// fires a <see cref="Changed"/> ping after each change. The <see cref="SnapshotStream"/>
/// subscribes to that ping and owns all observability. See ADR 0001.
/// </summary>
public abstract class Flow
{
    private FlowContext _ctx = null!;   // the run's working context, set at ExecuteAsync

    /// <summary>Fires after each step/phase change. The broadcaster subscribes; the snapshot isn't the flow's concern.</summary>
    public event Action? Changed;

    /// <summary>The recipe. Authors override this and compose steps via <see cref="RunStep"/> (L2-provisional).</summary>
    protected abstract Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct);

    /// <summary>Drive the run through its lifecycle on <paramref name="ctx"/> under <paramref name="config"/>. Called once.</summary>
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
        // No subscriber teardown here — the terminal snapshot completes the stream.
    }

    /// <summary>
    /// L2-PROVISIONAL step runner: run <paramref name="work"/> as a named step, recording
    /// status/timing/summary on the ledger and pinging around it. L3 hardens this to
    /// <c>Run&lt;T&gt;(Step&lt;T&gt;)</c> with the internal-only step-run seam.
    /// </summary>
    protected async Task<T> RunStep<T>(string name, Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        var rec = _ctx.AddStep(name);
        rec.Start();
        Changed?.Invoke();
        try
        {
            var result = await work(ct).ConfigureAwait(false);
            rec.Complete(result?.ToString());
            Changed?.Invoke();
            return result;
        }
        catch (Exception ex)
        {
            rec.Fail(ex.Message);
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
