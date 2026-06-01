namespace RemoteAgents.Flows;

/// <inheritdoc />
/// <remarks>Resolves the flow type from the container (<see cref="IServiceProvider"/>
/// is BCL — the engine takes no DI-package dependency).</remarks>
public sealed class FlowFactory(IServiceProvider services) : IFlowFactory
{
    public Flow Create(FlowDefinition definition) =>
        services.GetService(definition.FlowType) as Flow
        ?? throw new InvalidOperationException(
            $"Flow '{definition.Config.Name}' type {definition.FlowType.Name} is not registered as a Flow in DI.");
}
