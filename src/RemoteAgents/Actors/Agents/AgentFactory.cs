using RemoteAgents.Actors.Agents.Claude;
using RemoteAgents.Actors.Agents.Codex;

namespace RemoteAgents.Actors.Agents;

// Pre-UI the Interactive resolver is the non-interactive stub; Autonomous gets the auto-resolver.
public sealed class AgentFactory(IDecisionResolver humanResolver, AutoResolver autoResolver, AutoPolicy autoPolicy) : IAgentFactory
{
    public Agent Create(AgentConfig config, string projectDir) => config switch
    {
        FakeAgentConfig fake => new Agent(new FakeProvider(fake), projectDir),
        CodexConfig codex => new Agent(new CodexProvider(codex), projectDir),
        ClaudeConfig claude => new Agent(new ClaudeProvider(claude, ResolverFor(claude.Interactivity), autoPolicy), projectDir),
        _ => throw new NotSupportedException($"No provider for config type '{config.GetType().Name}'."),
    };

    private IDecisionResolver ResolverFor(Interactivity interactivity)
        => interactivity == Interactivity.Autonomous ? autoResolver : humanResolver;
}
