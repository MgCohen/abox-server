using Microsoft.Extensions.DependencyInjection;
using RemoteAgents.Events;
using RemoteAgents.Flows;

namespace RemoteAgents.Hosting;

// Builder type passed to services.AddRemoteAgents(opts => ...). Records the
// requested flow + sink set into the IServiceCollection.
//
// There is deliberately no UseClaude/UseCodex here. Agents are constructed,
// not resolved: a flow builds the agent it needs directly, and configured /
// named agents go through AgentPreset.Build(sink) — the one factory. The
// container owns flow dispatch (FlowRegistry + FlowRunner) and cross-cutting
// sinks, nothing provider-specific. See PLANS/architecture-refactor/99-rejected.md R13.
public sealed class RemoteAgentsOptions
{
    private readonly IServiceCollection _services;

    internal RemoteAgentsOptions(IServiceCollection services)
    {
        _services = services;
    }

    // Register an IEventSink with the container. The composite sink for
    // a given run is assembled by IEventSinkBuilder (Phase 7).
    public RemoteAgentsOptions AddSink<TSink>() where TSink : class, IEventSink
    {
        _services.AddSingleton<IEventSink, TSink>();
        return this;
    }

    // Register an IFlow with the FlowRegistry. The CLI dispatcher and
    // the Host both resolve flows from this single source of truth.
    public RemoteAgentsOptions AddFlow<TFlow>() where TFlow : class, IFlow
    {
        _services.AddSingleton<TFlow>();
        _services.AddSingleton<IFlow>(sp => sp.GetRequiredService<TFlow>());
        return this;
    }
}
