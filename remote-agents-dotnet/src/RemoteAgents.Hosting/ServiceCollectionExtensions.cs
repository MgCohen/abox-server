using Microsoft.Extensions.DependencyInjection;

namespace RemoteAgents.Hosting;

// Composition root for the RemoteAgents library. The Host calls this once
// at startup; the only thing it registers is the FlowCatalog (and the
// runtime FlowRegistry + IHistoryStore are registered by the Host directly).
//
// Per PLANS/architecture-refactor/08-composition.md: NO IConfiguration
// binding. Defaults live on the option records; overrides are lambdas at
// the registration site.
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRemoteAgents(this IServiceCollection services)
    {
        services.AddSingleton<FlowCatalog>();
        return services;
    }
}
