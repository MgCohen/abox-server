using RemoteAgents.Actors.Agents.Claude;
using RemoteAgents.Actors.Agents.Codex;

namespace RemoteAgents.Actors.Agents;

public sealed class AgentFactory(IQuestionResolver resolver) : IAgentFactory
{
    public Agent Create(AgentConfig config, string projectDir) => config switch
    {
        FakeAgentConfig fake => new Agent(new FakeProvider(fake), projectDir),
        CodexConfig codex => new Agent(new CodexProvider(codex), projectDir),
        ClaudeConfig claude => new Agent(new ClaudeProvider(claude, resolver), projectDir),
        _ => throw new NotSupportedException($"No provider for config type '{config.GetType().Name}'."),
    };
}
