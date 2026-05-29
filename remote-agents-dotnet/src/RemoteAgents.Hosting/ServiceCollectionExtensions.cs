using Microsoft.Extensions.DependencyInjection;
using RemoteAgents.Events;

namespace RemoteAgents.Hosting;

// Composition root for the RemoteAgents library. Hosts (CLI dispatcher,
// REST Host, test harness) call this once with a configure delegate.
//
// Per PLANS/architecture-refactor/08-composition.md: NO IConfiguration
// binding. Defaults live on the option records; overrides are lambdas at
// the registration site. If a single knob ever genuinely needs to differ
// per host, it reads from a named env var at the lambda body.
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRemoteAgents(
        this IServiceCollection services,
        Action<RemoteAgentsOptions> configure)
    {
        var opts = new RemoteAgentsOptions(services);
        configure(opts);
        return services;
    }

    // Host shells (REST Host, MAUI shell, test harness) register their
    // own transport sink (ChannelSink, CaptureSink, …) on top of the
    // library's default set. Kept as a separate extension so Host code
    // doesn't need a reference to the library-internal sink builder.
    public static IServiceCollection AddRemoteAgentSink<TSink>(this IServiceCollection services)
        where TSink : class, IEventSink
    {
        services.AddSingleton<IEventSink, TSink>();
        return services;
    }
}
