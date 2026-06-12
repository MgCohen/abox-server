namespace ABox.Domain.Agents;

public interface IAgentFactory
{
    Agent Create(AgentConfig config, string projectDir);
}
