namespace RemoteAgents.Actors.Agents;

public sealed record FakeAgentConfig(string Name, string Description, string Model, string SystemPrompt)
    : AgentConfig(Name, Description, Model, SystemPrompt);
