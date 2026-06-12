using Microsoft.Extensions.DependencyInjection;
using ABox.Domain.Flow;

namespace ABox.Features.Flows.Module;

public sealed class FlowFactory(IServiceProvider services) : IFlowFactory
{
    public Flow Create(FlowDefinition definition) =>
        (Flow)services.GetRequiredService(definition.FlowType);
}
