using RemoteAgents.Actors.Agents;

namespace RemoteAgents.Host;

public sealed class AgentFactory : IAgentFactory
{
    public Agent Create(AgentConfig config) => config switch
    {
        FakeAgentConfig fake => new Agent(fake, new FakeProvider(fake)),
        _ => throw new NotSupportedException($"No provider for config type '{config.GetType().Name}'."),
    };
}
