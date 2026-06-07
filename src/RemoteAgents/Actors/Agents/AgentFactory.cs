using RemoteAgents.Actors.Agents.Claude;
using RemoteAgents.Actors.Agents.Codex;

namespace RemoteAgents.Actors.Agents;

// Resolution selects the resolver: Auto self-answers, Human awaits the person (pre-UI
// the non-interactive stub). Deny + Llm are wired in their own build steps.
public sealed class AgentFactory(IDecisionResolver humanResolver, AutoResolver autoResolver, AutoPolicy autoPolicy) : IAgentFactory
{
    public Agent Create(AgentConfig config, string projectDir) => config switch
    {
        FakeAgentConfig fake => new Agent(new FakeProvider(fake), ResolverFor(config), CapFor(config), projectDir),
        CodexConfig codex => new Agent(new CodexProvider(codex), ResolverFor(config), CapFor(config), projectDir),
        ClaudeConfig claude => new Agent(new ClaudeProvider(claude, ResolverFor(config), autoPolicy), ResolverFor(config), CapFor(config), projectDir),
        _ => throw new NotSupportedException($"No provider for config type '{config.GetType().Name}'."),
    };

    private IDecisionResolver ResolverFor(AgentConfig config) => config.Resolution switch
    {
        Resolution.Auto => autoResolver,
        Resolution.Human => humanResolver,
        var r => throw new NotSupportedException($"Resolution.{r} is not wired yet."),
    };

    // Cap the resolvers that always produce an answer and could loop forever; the
    // self-terminating ones (Deny/Human return null when done) run uncapped.
    private static int? CapFor(AgentConfig config) => config.Resolution switch
    {
        Resolution.Auto or Resolution.Llm => config.ResolveCap,
        _ => null,
    };
}
