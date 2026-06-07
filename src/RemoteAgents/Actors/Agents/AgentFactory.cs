using RemoteAgents.Actors.Agents.Claude;
using RemoteAgents.Actors.Agents.Codex;

namespace RemoteAgents.Actors.Agents;

// Pre-UI the Interactive resolver is the non-interactive stub; Autonomous gets the auto-resolver.
public sealed class AgentFactory(IDecisionResolver humanResolver, AutoResolver autoResolver, AutoPolicy autoPolicy) : IAgentFactory
{
    public Agent Create(AgentConfig config, string projectDir) => config switch
    {
        FakeAgentConfig fake => new Agent(new FakeProvider(fake), ResolverFor(config), CapFor(config), projectDir),
        CodexConfig codex => new Agent(new CodexProvider(codex), ResolverFor(config), CapFor(config), projectDir),
        ClaudeConfig claude => new Agent(new ClaudeProvider(claude, ResolverFor(config), autoPolicy), ResolverFor(config), CapFor(config), projectDir),
        _ => throw new NotSupportedException($"No provider for config type '{config.GetType().Name}'."),
    };

    private IDecisionResolver ResolverFor(AgentConfig config)
        => config.Interactivity == Interactivity.Autonomous ? autoResolver : humanResolver;

    // The auto-resolver never self-terminates, so cap its loop; a human returns null when done.
    private static int? CapFor(AgentConfig config)
        => config.Interactivity == Interactivity.Autonomous ? config.ResolveCap : null;
}
