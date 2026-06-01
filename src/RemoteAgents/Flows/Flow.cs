using RemoteAgents.Contracts;

namespace RemoteAgents.Flows;

/// <summary>
/// A flow recipe and its orchestration. Authors override <see cref="RunAsync"/> to
/// compose steps; <see cref="ExecuteAsync"/> drives the lifecycle (Pending → Running
/// → terminal). Its <see cref="Config"/> and collaborators (agents, validators, git,
/// as they arrive at L5–L8) are constructor-injected; it holds no run-state — that
/// lives in the <see cref="FlowContext"/> it's handed. So the recipe is stateless
/// w.r.t. a run and the registry tracks contexts, not flows; run inputs come in on
/// the context. See ADR 0001.
/// </summary>
public abstract class Flow
{
    protected Flow(FlowConfig config) => Config = config;

    /// <summary>This flow's configuration — identity now, behavioural knobs + polymorphism later.
    /// Immutable, constructor-injected; the recipe reads it, the context never holds it.</summary>
    public FlowConfig Config { get; }

    /// <summary>The recipe. Authors override this and compose steps via <c>ctx.RunStep</c> (L2-provisional).</summary>
    protected abstract Task RunAsync(FlowContext ctx, CancellationToken ct);

    /// <summary>Drive the run through its lifecycle on <paramref name="ctx"/>. The registry calls this once.</summary>
    public async Task ExecuteAsync(FlowContext ctx, CancellationToken ct)
    {
        ctx.SetPhase(FlowPhase.Running);
        try
        {
            await RunAsync(ctx, ct).ConfigureAwait(false);
            ctx.SetPhase(FlowPhase.Completed);
        }
        catch (OperationCanceledException)
        {
            ctx.SetPhase(FlowPhase.Canceled);
        }
        catch
        {
            ctx.SetPhase(FlowPhase.Failed);
            throw;
        }
        finally
        {
            ctx.CompleteSubscribers();
        }
    }
}
