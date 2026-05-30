using RemoteAgents.Agents;
using RemoteAgents.Events;
using RemoteAgents.Providers.Claude;
using RemoteAgents.Providers.Codex;

namespace RemoteAgents.Hosting;

// Polymorphic preset — a value that describes one agent configuration
// (name, model, system prompt). Each subtype encodes its provider via
// type identity, so there is no "provider" string field anywhere on the
// runtime. Callers obtain a configured agent via `preset.Build(sink)`.
//
// No IServiceProvider parameter on Build: presets are self-contained
// values. If a future use case needs DI-injected defaults the overload
// can land then — see 99-rejected.md R14.
public abstract record AgentPreset(string Name)
{
    public abstract Agent Build(IEventSink sink);
}

public sealed record ClaudePreset(
    string Name,
    string? Model = null,
    string? SystemPrompt = null)
    : AgentPreset(Name)
{
    public override Agent Build(IEventSink sink) =>
        new ClaudeAgent
        {
            Name = Name,
            Sink = sink,
            Options = new ClaudeAgentOptions(Model: Model, SystemPrompt: SystemPrompt),
        };
}

public sealed record CodexPreset(
    string Name,
    string? Model = null,
    string? SystemPrompt = null,
    string? Sandbox = null)
    : AgentPreset(Name)
{
    public override Agent Build(IEventSink sink) =>
        new CodexAgent
        {
            Name = Name,
            Sink = sink,
            Options = Sandbox is null
                ? new CodexAgentOptions(Model: Model, SystemPrompt: SystemPrompt)
                : new CodexAgentOptions(Sandbox: Sandbox, Model: Model, SystemPrompt: SystemPrompt),
        };
}
