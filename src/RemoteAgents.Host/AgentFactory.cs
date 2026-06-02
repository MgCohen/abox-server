using RemoteAgents.Actors.Agents;

namespace RemoteAgents.Host;

public sealed class AgentFactory : IAgentFactory
{
    public Agent Create(AgentConfig config) => config switch
    {
        FakeAgentConfig fake => new FakeAgent(fake),
        _ => throw new NotSupportedException($"No agent for config type '{config.GetType().Name}'."),
    };
}
