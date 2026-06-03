namespace RemoteAgents.Actors.Agents.Codex;

public sealed record CodexConfig(
    string Name,
    string Description,
    string Model,
    string SystemPrompt,
    string Sandbox = "workspace-write",
    int JsonStreamTimeoutMs = 60_000)
    : AgentConfig(Name, Description, Model, SystemPrompt);
