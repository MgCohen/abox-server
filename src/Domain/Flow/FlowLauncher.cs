using RemoteAgents.Contracts;

namespace RemoteAgents.Domain.Flow;

public sealed class FlowLauncher(FlowCatalog catalog, IFlowFactory factory, FlowRegistry registry)
{
    public Guid? Start(string flowName, string project, string projectDir, string prompt)
    {
        var def = catalog.Resolve(flowName);
        if (def is null) return null;

        var flow = factory.Create(def);
        var ctx = new FlowContext(def.Config.Name, project, projectDir, prompt);
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
