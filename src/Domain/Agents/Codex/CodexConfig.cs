namespace RemoteAgents.Domain.Agents.Codex;

public sealed record CodexConfig(
    string Name,
    string Description,
    string Model,
    string SystemPrompt,
    int JsonStreamTimeoutMs = 60_000)
    : AgentConfig(Name, Description, Model, SystemPrompt);
