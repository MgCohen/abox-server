namespace RemoteAgents.Domain.Agents;

public sealed record FakeAgentConfig(string Name, string Description, string Model, string SystemPrompt, string? Reply = null)
    : AgentConfig(Name, Description, Model, SystemPrompt);
