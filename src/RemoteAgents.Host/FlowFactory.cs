using Microsoft.Extensions.DependencyInjection;
using RemoteAgents.Flows;

namespace RemoteAgents.Host;

/// <inheritdoc />
/// <remarks>
/// Constructs the flow via <see cref="ActivatorUtilities"/>: the definition's
/// <see cref="FlowConfig"/> is passed to the ctor, and any service/tooling ctor deps
/// resolve from the container. No manual <c>new</c> at composition (R-SPINE-2). Lives
/// in the Host because it's DI-coupled; the engine keeps only the <see cref="IFlowFactory"/>
/// seam (DI-package-free).
/// </remarks>
public sealed class FlowFactory(IServiceProvider services) : IFlowFactory
{
    public Flow Create(FlowDefinition definition) =>
        (Flow)ActivatorUtilities.CreateInstance(services, definition.FlowType, definition.Config);
}
