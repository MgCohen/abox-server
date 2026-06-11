using RemoteAgents.Domain.Agents.Claude;
using RemoteAgents.Domain.Agents.Codex;

namespace RemoteAgents.Domain.Agents;

// Maps a config to its provider; the resolver + loop cap come from ResolverSelector (keyed on Resolution).
public sealed class AgentFactory(ResolverSelector resolvers, AutoPolicy autoPolicy) : IAgentFactory
{
    public Agent Create(AgentConfig config, string projectDir)
    {
        var (resolver, cap) = resolvers.For(config);
        return config switch
        {
            CodexConfig codex => new Agent(new CodexProvider(codex), resolver, cap, projectDir),
            ClaudeConfig claude => new Agent(new ClaudeProvider(claude, resolver, autoPolicy), resolver, cap, projectDir),
            _ => throw new NotSupportedException($"No provider for config type '{config.GetType().Name}'."),
        };
    }
}
