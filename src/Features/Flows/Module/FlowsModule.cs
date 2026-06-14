using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ABox.Domain.Flow;
using ABox.Features.Flows.Cancel;
using ABox.Features.Flows.Catalog;
using ABox.Features.Flows.Get;
using ABox.Features.Flows.List;
using ABox.Features.Flows.Start;
using ABox.Features.Flows.Watch;

namespace ABox.Features.Flows.Module;

public static class FlowsModule
{
    public static IServiceCollection AddFlows(this IServiceCollection services, Action<FlowCatalog>? register = null)
    {
        services.AddSingleton<IFlowHistory, FileFlowHistory>();
        services.AddSingleton<FlowRegistry>();
        services.AddSingleton<FlowLauncher>();
        services.AddSingleton<IFlowFactory, FlowFactory>();
        services.AddSingleton<ProjectDirectory>();

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
