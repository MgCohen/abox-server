using Microsoft.Extensions.DependencyInjection;
using RemoteAgents.Domain.Flow;

namespace RemoteAgents.Features.Flows.Module;

public sealed class FlowFactory(IServiceProvider services) : IFlowFactory
{
    public Flow Create(FlowDefinition definition) =>
        (Flow)services.GetRequiredService(definition.FlowType);
}
