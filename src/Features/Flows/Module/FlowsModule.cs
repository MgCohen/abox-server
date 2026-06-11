using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RemoteAgents.Domain.Flow;
using RemoteAgents.Features.Flows.Cancel;
using RemoteAgents.Features.Flows.Catalog;
using RemoteAgents.Features.Flows.Get;
using RemoteAgents.Features.Flows.List;
using RemoteAgents.Features.Flows.Start;
using RemoteAgents.Features.Flows.Watch;

namespace RemoteAgents.Features.Flows.Module;

public static class FlowsModule
{
    public static IServiceCollection AddFlows(this IServiceCollection services, Action<FlowCatalog>? register = null)
    {
        services.AddSingleton<IFlowHistory, FileFlowHistory>();
        services.AddSingleton<FlowRegistry>();
        services.AddSingleton<FlowLauncher>();
        services.AddSingleton<IFlowFactory, FlowFactory>();

        // Flows are content, not engine: the composition root supplies the catalog (empty in production
        // today). Eager build → fail-fast on a bad entry. Flows are stateless (config is a run arg), so
        // transient. See ADR 0001.
        var catalog = new FlowCatalog();
        register?.Invoke(catalog);
        foreach (var def in catalog.All())
            services.AddTransient(def.FlowType);
        services.AddSingleton(catalog);

        return services;
    }

    public static void MapFlows(this IEndpointRouteBuilder app)
    {
        CatalogEndpoint.Map(app);

        var flows = app.MapGroup("/flows");
        StartEndpoint.Map(flows);
        ListEndpoint.Map(flows);
        GetEndpoint.Map(flows);
        CancelEndpoint.Map(flows);
        WatchEndpoint.Map(flows);
    }
}
