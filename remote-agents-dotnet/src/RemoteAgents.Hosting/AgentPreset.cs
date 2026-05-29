using Microsoft.Extensions.DependencyInjection;
using RemoteAgents.Agents;
using RemoteAgents.Events;

namespace RemoteAgents.Hosting;

// Polymorphic preset — a value that describes one agent configuration
// (model, prompt, hook policy, …). Each subtype encodes its provider via
// type identity, so there is no "provider" string field anywhere on the
// runtime. Callers obtain a configured agent via `preset.Build(sp, sink)`.
//
// Build pulls library dependencies (ClaudeAgent, CodexAgent) out of the
// service provider via ActivatorUtilities, so the agent's own ctor can
// be expanded later without touching every preset site.
public abstract record AgentPreset(string Name)
{
    public abstract Agent Build(IServiceProvider sp, IEventSink sink);
}

public sealed record ClaudePreset(
    string Name,
    string? Model = null,
    string? SystemPrompt = null,
    InteractionMode Mode = InteractionMode.NonInteractive)
    : AgentPreset(Name)
{
    public override Agent Build(IServiceProvider sp, IEventSink sink)
    {
        var baseOpts = sp.GetService<ClaudeAgentOptions>() ?? new ClaudeAgentOptions();
        var opts = baseOpts with
        {
            Model        = Model        ?? baseOpts.Model,
            SystemPrompt = SystemPrompt ?? baseOpts.SystemPrompt,
        };
        return new ClaudeAgent
        {
            Name = Name,
            Sink = sink,
            Options = opts,
        };
    }
}

public sealed record CodexPreset(
    string Name,
    string? Model = null,
    string? SystemPrompt = null,
    string? Sandbox = null,
    InteractionMode Mode = InteractionMode.NonInteractive)
    : AgentPreset(Name)
{
    public override Agent Build(IServiceProvider sp, IEventSink sink)
    {
        var baseOpts = sp.GetService<CodexAgentOptions>() ?? new CodexAgentOptions();
        var opts = baseOpts with
        {
            Model        = Model        ?? baseOpts.Model,
            SystemPrompt = SystemPrompt ?? baseOpts.SystemPrompt,
            Sandbox      = Sandbox      ?? baseOpts.Sandbox,
        };
        return new CodexAgent
        {
            Name = Name,
            Sink = sink,
            Options = opts,
        };
    }
}
