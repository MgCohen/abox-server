using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using RemoteAgents.Engine.Flows;
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
        services.AddSingleton<IFlowHistory, FileFlowHistory>();
        services.AddSingleton<FlowRegistry>();
        services.AddSingleton<FlowLauncher>();
        services.AddSingleton<IFlowFactory, FlowFactory>();
        services.AddSingleton<PendingDecisions>();
        services.AddSingleton<IDecisionResolver, InteractiveResolver>();
        services.AddSingleton<AutoResolver>();
        services.AddSingleton<DenyResolver>();
        services.AddSingleton<AutoPolicy>();
        services.AddSingleton<IAgentFactory, AgentFactory>();

        // Eager build → fail-fast on a bad entry. Flows are stateless (config is a run arg), so transient. See ADR 0001.
        var catalog = new FlowCatalog();
        catalog.Register<StubFlow>(new FlowConfig("stub", "Walking-skeleton stub: placeholder steps, no real work."));
        catalog.Register<CodexPingFlow>(new FlowConfig("codex-ping", "Drive the Codex reviewer with the run prompt."));
        catalog.Register<ClaudePingFlow>(new FlowConfig("claude-ping", "Drive the Claude implementer with the run prompt."));
        foreach (var def in catalog.All())
            services.AddTransient(def.FlowType);
        services.AddSingleton(catalog);
    }
}
