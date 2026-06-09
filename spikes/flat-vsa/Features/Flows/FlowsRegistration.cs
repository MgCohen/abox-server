using App.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace App.Features.Flows;

public static class FlowsRegistration
{
    public static IServiceCollection AddFlows(this IServiceCollection services)
    {
        services.AddSingleton<IFlowEngine, StubFlowEngine>();
        services.AddTransient<IApiHandler<RunFlowRequest, RunFlowResponse>, RunFlowHandler>();
        services.AddTransient<IApiHandler<GetFlowSnapshotRequest, FlowSnapshotDto?>, GetFlowSnapshotHandler>();
        return services;
    }
}
