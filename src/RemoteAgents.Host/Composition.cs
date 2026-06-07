using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using RemoteAgents.Flows;
using RemoteAgents.Actors.Agents;
using RemoteAgents.Actors.Agents.Claude;
using RemoteAgents.Tools.Paths;
using RemoteAgents.Tools.Projects;

namespace RemoteAgents.Host;

internal static class Composition
{
    public const string CorsPolicy = "open";

    public static void AddServices(WebApplicationBuilder builder)
    {
        var services = builder.Services;

        // Transport is Tailscale-only with no app-layer auth (feature-map A8), so CORS is wide open.
        services.AddCors(o => o.AddPolicy(CorsPolicy, p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddSingleton<IOrchestratorPaths, OrchestratorPaths>();
        services.AddSingleton<IProjectRegistry, ProjectRegistry>();
        services.AddSingleton<IHistoryStore, FileHistoryStore>();
        services.AddSingleton<FlowRegistry>();
        services.AddSingleton<FlowLauncher>();
        services.AddSingleton<IFlowFactory, FlowFactory>();
        services.AddSingleton<IQuestionResolver, NonInteractiveResolver>();
        services.AddSingleton<AutoPolicy>();
        services.AddSingleton<IAgentFactory, AgentFactory>();

        // Eager build → fail-fast on a bad entry. Flows are stateless (config is a run arg), so transient. See ADR 0001.
        var catalog = FlowCatalog.Build();
        foreach (var def in catalog.All())
            services.AddTransient(def.FlowType);
        services.AddSingleton(catalog);
    }
}
