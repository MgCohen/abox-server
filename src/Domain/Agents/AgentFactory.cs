using RemoteAgents.Domain.Agents.Claude;
using RemoteAgents.Domain.Agents.Codex;

namespace RemoteAgents.Domain.Agents;

// Resolution selects the resolver: Auto self-answers, Deny refuses, Human awaits the
// person via the injected resolver. Llm is reserved (deferred).
public sealed class AgentFactory(IDecisionResolver humanResolver, AutoResolver autoResolver, DenyResolver denyResolver, AutoPolicy autoPolicy) : IAgentFactory
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
        Resolution.Deny => denyResolver,
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
