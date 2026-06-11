using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RemoteAgents.Domain.Flow;
using RemoteAgents.Features.Flows.Cancel;
using RemoteAgents.Features.Flows.Catalog;
using RemoteAgents.Features.Flows.Definitions;
using RemoteAgents.Features.Flows.Get;
using RemoteAgents.Features.Flows.List;
using RemoteAgents.Features.Flows.Start;
using RemoteAgents.Features.Flows.Watch;

namespace RemoteAgents.Features.Flows.Module;

public static class FlowsModule
{
    public static IServiceCollection AddFlows(this IServiceCollection services)
    {
        services.AddSingleton<IFlowHistory, FileFlowHistory>();
        services.AddSingleton<FlowRegistry>();
        services.AddSingleton<FlowLauncher>();
        services.AddSingleton<IFlowFactory, FlowFactory>();

        // Eager build → fail-fast on a bad entry. Flows are stateless (config is a run arg), so transient. See ADR 0001.
        var catalog = new FlowCatalog();
        catalog.Register<StubFlow>(new FlowConfig("stub", "Walking-skeleton stub: placeholder steps, no real work."));
        catalog.Register<CodexPingFlow>(new FlowConfig("codex-ping", "Drive the Codex reviewer with the run prompt."));
        catalog.Register<ClaudePingFlow>(new FlowConfig("claude-ping", "Drive the Claude implementer with the run prompt."));
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
