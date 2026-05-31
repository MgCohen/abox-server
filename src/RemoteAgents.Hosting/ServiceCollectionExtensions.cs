using Microsoft.Extensions.DependencyInjection;
using RemoteAgents.Core.Paths;
using RemoteAgents.Core.Projects;
using RemoteAgents.Flows;

namespace RemoteAgents.Hosting;

/// <summary>Composition root for the orchestrator's services.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Register the orchestrator services (infra + flow tech).</summary>
    public static IServiceCollection AddRemoteAgents(this IServiceCollection services)
    {
        services.AddSingleton<IOrchestratorPaths, OrchestratorPaths>();
        services.AddSingleton<IProjectRegistry, ProjectRegistry>();
        services.AddSingleton<FlowCatalog>();
        services.AddSingleton<IHistoryStore, FileHistoryStore>();
        services.AddSingleton<FlowRegistry>();
        return services;
    }

    /// <summary>
    /// Register a flow recipe: a transient service resolved per run, plus a catalog
    /// entry (name → Type). The composition root lists these once; no flow instance
    /// is ever <c>new</c>-ed in a lambda (R-SPINE-2).
    /// </summary>
    public static IServiceCollection AddFlow<TFlow>(this IServiceCollection services, string name, string description)
        where TFlow : Flow
    {
        services.AddTransient<TFlow>();
        services.AddSingleton(new FlowRegistration(name, description, typeof(TFlow)));
        return services;
    }
}
