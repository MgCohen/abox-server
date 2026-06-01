using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using RemoteAgents.Flows;
using RemoteAgents.Paths;
using RemoteAgents.Projects;

namespace RemoteAgents.Host;

/// <summary>
/// Service registration + setup — the composition root. Wires the engine services and
/// builds the flow catalog eagerly, so a bad catalog entry is a fail-fast boot error
/// (ADR 0001). Companion to <see cref="Endpoints"/>, which maps the HTTP surface.
/// </summary>
internal static class Composition
{
    // Transport is Tailscale-only; CORS is wide open so a separate-origin WASM bundle
    // can call the Host. No app-layer auth (feature-map A8).
    public const string CorsPolicy = "open";

    public static void AddServices(WebApplicationBuilder builder)
    {
        var services = builder.Services;

        services.AddCors(o => o.AddPolicy(CorsPolicy, p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        // String enums on the wire (FlowPhase/StepStatus render as names).
        services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        services.AddSingleton<IOrchestratorPaths, OrchestratorPaths>();
        services.AddSingleton<IProjectRegistry, ProjectRegistry>();
        services.AddSingleton<IHistoryStore, FileHistoryStore>();
        services.AddSingleton<FlowRegistry>();
        services.AddSingleton<IFlowFactory, FlowFactory>();

        // FlowCatalog.Build() runs Register + boot guard now → fail-fast on a bad entry.
        // Flows are stateless recipes (config is an execution arg), so each catalog type
        // is a plain transient the factory resolves by type. See ADR 0001.
        var catalog = FlowCatalog.Build();
        foreach (var def in catalog.All())
            services.AddTransient(def.FlowType);
        services.AddSingleton(catalog);
    }
}
