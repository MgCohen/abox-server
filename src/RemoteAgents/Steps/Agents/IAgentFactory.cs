namespace RemoteAgents.Steps.Agents;

public interface IAgentFactory
{
    Agent Create(string role, string name, string prompt, string? sessionId = null);
}
