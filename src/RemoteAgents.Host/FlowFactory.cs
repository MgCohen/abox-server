using Microsoft.Extensions.DependencyInjection;
using RemoteAgents.Flows;

namespace RemoteAgents.Host;

/// <inheritdoc />
/// <remarks>
/// Resolves the flow by type from the container — flows are plain DI types now that
/// config is an execution argument, not a ctor dep, so any service/tooling ctor deps
/// (agents, git, as they arrive at L5–L8) resolve normally. No manual <c>new</c> at
/// composition (R-SPINE-2). Lives in the Host because it's DI-coupled; the engine keeps
/// only the <see cref="IFlowFactory"/> seam (DI-package-free).
/// </remarks>
public sealed class FlowFactory(IServiceProvider services) : IFlowFactory
{
    public Flow Create(FlowDefinition definition) =>
        (Flow)services.GetRequiredService(definition.FlowType);
}
