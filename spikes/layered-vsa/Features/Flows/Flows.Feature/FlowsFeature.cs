using Flows.Contracts;
using Flows.GetFlowSnapshot;
using Flows.RunFlow;
using Infra.AgentRuntime;
using Microsoft.Extensions.DependencyInjection;

namespace Flows;

public static class FlowsFeature
{
    public static IServiceCollection AddFlows(this IServiceCollection services)
    {
        services.AddSingleton<IFlowEngine, StubFlowEngine>();
        services.AddTransient<IApiHandler<RunFlowRequest, RunFlowResponse>, RunFlowHandler>();
        services.AddTransient<IApiHandler<GetFlowSnapshotRequest, FlowSnapshotDto?>, GetFlowSnapshotHandler>();
        return services;
    }
}
