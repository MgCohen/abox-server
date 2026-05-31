using Microsoft.Extensions.DependencyInjection;
using RemoteAgents.Core.Paths;
using RemoteAgents.Core.Projects;

namespace RemoteAgents.Hosting;

/// <summary>Composition root for the orchestrator's services.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register the orchestrator services. At L1 this is the generic infra only
    /// (paths + project registry); later layers extend it.
    /// </summary>
    public static IServiceCollection AddRemoteAgents(this IServiceCollection services)
    {
        services.AddSingleton<IOrchestratorPaths, OrchestratorPaths>();
        services.AddSingleton<IProjectRegistry, ProjectRegistry>();
        return services;
    }
}
