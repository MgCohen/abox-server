using RemoteAgents.Actors.Agents.Codex;

namespace RemoteAgents.Actors.Agents;

public sealed class AgentFactory : IAgentFactory
{
    public Agent Create(AgentConfig config) => config switch
    {
        FakeAgentConfig fake => new Agent(fake, new FakeProvider(fake)),
        CodexConfig codex => new Agent(codex, new CodexProvider(codex)),
        _ => throw new NotSupportedException($"No provider for config type '{config.GetType().Name}'."),
    };
}
