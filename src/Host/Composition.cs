using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using RemoteAgents.Domain.Agents;
using RemoteAgents.Domain.Agents.Claude;
using RemoteAgents.Features.Flows.Module;
using RemoteAgents.Infrastructure.Paths;
using RemoteAgents.Infrastructure.Projects;

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

        services.AddSingleton<PendingDecisions>();
        services.AddSingleton<IDecisionResolver, InteractiveResolver>();
        services.AddSingleton<AutoResolver>();
        services.AddSingleton<DenyResolver>();
        services.AddSingleton<AutoPolicy>();
        services.AddSingleton<IAgentFactory, AgentFactory>();

        services.AddFlows();
    }
}
