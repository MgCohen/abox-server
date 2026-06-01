using RemoteAgents.Contracts;

namespace RemoteAgents.Flows;

/// <summary>
/// The one entry point that starts a run: resolve a flow by name, build it via the
/// factory, create its context, register it, and drive it on a background task. Holds
/// no run-state itself — the <see cref="FlowRegistry"/> is the ledger of live + past runs.
/// </summary>
public sealed class FlowLauncher(FlowCatalog catalog, IFlowFactory factory, FlowRegistry registry)
{
    /// <summary>Launch a run by flow name. Returns the run id, or null if no such flow.</summary>
    public Guid? Start(string flowName, string project, string projectDir, string prompt, string[] args)
    {
        var def = catalog.Resolve(flowName);
        if (def is null) return null;

        var flow = factory.Create(def);
        var ctx = new FlowContext(def.Config.Name, project, projectDir, prompt, args);
        var stream = new SnapshotStream(flow, ctx);
        var ct = registry.Track(ctx, stream);

        _ = Task.Run(() => Drive(flow, def.Config, ctx, ct));
        return ctx.Id;
    }

    private async Task Drive(Flow flow, FlowConfig config, FlowContext ctx, CancellationToken ct)
    {
        try { await flow.ExecuteAsync(config, ctx, ct); }
        catch { /* terminal phase already recorded on the context */ }
        finally { await registry.Complete(ctx); }
    }
}
