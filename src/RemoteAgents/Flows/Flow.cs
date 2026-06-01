using RemoteAgents.Contracts;

namespace RemoteAgents.Flows;

/// <summary>
/// A flow recipe and its orchestration. Authors override <see cref="RunAsync"/> to
/// compose steps; <see cref="ExecuteAsync"/> drives the lifecycle (Pending → Running
/// → terminal). A Flow holds no run-state — that lives in the <see cref="FlowContext"/>
/// it's handed, so the recipe is effectively stateless and the registry tracks
/// contexts, not flows. Collaborators (agents, validators, git) are constructor-
/// injected as they arrive (L5–L8); run inputs come in on the context. See ADR 0001.
/// </summary>
public abstract class Flow
{
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
