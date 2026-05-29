namespace RemoteAgents.Hosting;

// Named agent presets. Each field is a polymorphic AgentPreset value;
// the subtype encodes the provider, the field name encodes the role.
// Replaces the per-named-agent static Create() methods that previously
// lived under src/NamedAgents/ — same shape, single source of truth.
//
// Prompts are read from src/RemoteAgents.Hosting/prompts/<name>.md at
// Build() time so editing markdown picks up on the next run with no
// rebuild (see Prompts.cs).
//
// Usage from a flow:
//   var planner = AgentPresets.Planner.Build(sink);
public static class AgentPresets
{
    public static readonly ClaudePreset Planner = new(
        Name: "planner",
        Model: "opus",
        SystemPrompt: Prompts.Load("planner"));

    public static readonly ClaudePreset Documenter = new(
        Name: "documenter",
        Model: "haiku",
        SystemPrompt: Prompts.Load("documenter"));

    public static readonly CodexPreset Researcher = new(
        Name: "researcher",
        Model: "gpt-5.5",
        SystemPrompt: Prompts.Load("researcher"));
}
