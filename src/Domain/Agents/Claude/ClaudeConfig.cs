namespace ABox.Domain.Agents.Claude;

public sealed record ClaudeConfig(
    string Name,
    string Description,
    string Model,
    string SystemPrompt,
    PermissionPolicy Policy = PermissionPolicy.Bypass)
    : AgentConfig(Name, Description, Model, SystemPrompt, Policy);
