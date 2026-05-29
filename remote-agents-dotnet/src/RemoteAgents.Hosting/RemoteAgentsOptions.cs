using Microsoft.Extensions.DependencyInjection;
using RemoteAgents.Agents;
using RemoteAgents.Events;

namespace RemoteAgents.Hosting;

// Builder type passed to services.AddRemoteAgents(opts => ...). Records
// the requested provider/sink/flow set into the IServiceCollection.
//
// No IConfiguration binding — options are records with defaults; per-host
// overrides go through the lambda passed to UseClaude / UseCodex (callers
// use a `with` expression so the records stay immutable). See
// PLANS/architecture-refactor/99-rejected.md R13.
public sealed class RemoteAgentsOptions
{
    private readonly IServiceCollection _services;

    internal RemoteAgentsOptions(IServiceCollection services)
    {
        _services = services;
    }

    // Register the Claude provider with its options. The optional configure
    // delegate transforms the default record:
    //
    //   opts.UseClaude(o => o with { Model = "opus" });
    //
    // Reading from an env var at the override site is the supported way
    // to vary a single knob per host without dragging in IConfiguration.
    public RemoteAgentsOptions UseClaude(Func<ClaudeAgentOptions, ClaudeAgentOptions>? configure = null)
    {
        var opts = configure?.Invoke(new ClaudeAgentOptions()) ?? new ClaudeAgentOptions();
        _services.AddSingleton(opts);
        _services.AddSingleton<IHookInstaller<ClaudeAgent>, ClaudeHookInstaller>();
        return this;
    }

    public RemoteAgentsOptions UseCodex(Func<CodexAgentOptions, CodexAgentOptions>? configure = null)
    {
        var opts = configure?.Invoke(new CodexAgentOptions()) ?? new CodexAgentOptions();
        _services.AddSingleton(opts);
        _services.AddSingleton<IHookInstaller<CodexAgent>, CodexHookInstaller>();
        return this;
    }

    // Register an IEventSink with the container. The composite sink for
    // a given run is assembled by IEventSinkBuilder (Phase 7).
    public RemoteAgentsOptions AddSink<TSink>() where TSink : class, IEventSink
    {
        _services.AddSingleton<IEventSink, TSink>();
        return this;
    }

    // Flow registration is wired up in Phase 4 once IFlow exists. The
    // method shape lands now so call sites in Program.cs can adopt the
    // builder pattern; the body just registers the type as a singleton
    // until Phase 4 introduces IFlowRegistry.
    public RemoteAgentsOptions AddFlow<TFlow>() where TFlow : class
    {
        _services.AddSingleton<TFlow>();
        return this;
    }
}
