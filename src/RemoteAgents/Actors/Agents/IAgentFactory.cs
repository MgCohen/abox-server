namespace RemoteAgents.Actors.Agents;

public interface IAgentFactory
{
    Agent Create(AgentConfig config);
}
