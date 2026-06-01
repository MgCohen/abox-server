using RemoteAgents.Contracts;

namespace RemoteAgents.Flows;

/// <summary>
/// A flow recipe and its orchestration. Authors override <see cref="RunAsync"/> to
/// compose steps; <see cref="ExecuteAsync"/> drives the lifecycle (Pending → Running
/// → terminal). The flow is a fully stateless recipe: it holds no config and no
/// run-state. Its <see cref="FlowConfig"/> arrives as an execution argument (sourced
/// from the catalog definition at launch) and its run-state lives in the
/// <see cref="FlowContext"/> it's handed; collaborators (agents, validators, git, as
/// they arrive at L5–L8) are the only constructor-injected deps. So the registry
/// resolves a flow by type and tracks contexts, not flows. See ADR 0001.
/// </summary>
public abstract class Flow
{
    /// <summary>The recipe. Authors override this and compose steps via <c>ctx.RunStep</c> (L2-provisional).</summary>
    protected abstract Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct);

    /// <summary>Drive the run through its lifecycle on <paramref name="ctx"/> under <paramref name="config"/>. The registry calls this once.</summary>
    public async Task ExecuteAsync(FlowConfig config, FlowContext ctx, CancellationToken ct)
    {
        ctx.SetPhase(FlowPhase.Running);
        try
        {
            await RunAsync(config, ctx, ct).ConfigureAwait(false);
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
