namespace RemoteAgents.Actors.Agents.Claude;

public sealed record ClaudeConfig(
    string Name,
    string Description,
    string Model,
    string SystemPrompt,
    string PermissionMode = "acceptEdits")
    : AgentConfig(Name, Description, Model, SystemPrompt);
