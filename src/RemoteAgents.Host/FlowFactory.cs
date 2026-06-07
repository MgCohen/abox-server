using Microsoft.Extensions.DependencyInjection;
using RemoteAgents.Engine.Flows;

namespace RemoteAgents.Host;

public sealed class FlowFactory(IServiceProvider services) : IFlowFactory
{
    public Flow Create(FlowDefinition definition) =>
        (Flow)services.GetRequiredService(definition.FlowType);
}
