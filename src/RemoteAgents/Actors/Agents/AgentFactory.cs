using RemoteAgents.Actors.Agents.Claude;
using RemoteAgents.Actors.Agents.Codex;

namespace RemoteAgents.Actors.Agents;

// The human/stub resolver serves Interactive agents (pre-UI it is the non-interactive
// stub); the auto-resolver serves Autonomous agents. The flag picks which the provider gets.
public sealed class AgentFactory(IDecisionResolver humanResolver, AutoResolver autoResolver, AutoPolicy autoPolicy) : IAgentFactory
{
    public Agent Create(AgentConfig config, string projectDir) => config switch
    {
        FakeAgentConfig fake => new Agent(new FakeProvider(fake), projectDir),
        CodexConfig codex => new Agent(new CodexProvider(codex), projectDir),
        ClaudeConfig claude => new Agent(new ClaudeProvider(claude, ResolverFor(claude), autoPolicy), projectDir),
        _ => throw new NotSupportedException($"No provider for config type '{config.GetType().Name}'."),
    };

    private IDecisionResolver ResolverFor(AgentConfig config)
        => config.Interactivity == Interactivity.Autonomous ? autoResolver : humanResolver;
}
