namespace RemoteAgents.Flows;

/// <inheritdoc />
/// <remarks>Resolves the flow type from the container (<see cref="IServiceProvider"/>
/// is BCL — the engine takes no DI-package dependency) and binds its config.</remarks>
public sealed class FlowFactory(FlowCatalog catalog, IServiceProvider services) : IFlowFactory
{
    public Flow? Create(string name)
    {
        var def = catalog.Resolve(name);
        if (def is null) return null;

        var flow = services.GetService(def.FlowType) as Flow
            ?? throw new InvalidOperationException(
                $"Flow '{name}' resolves to {def.FlowType.Name}, which is not registered as a Flow in DI.");
        flow.Configure(def.Config);
        return flow;
    }
}
