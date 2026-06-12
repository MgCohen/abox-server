using ABox.Domain.Agents;

namespace ABox.Tests.Support;

// Test IAgentFactory that wraps an injected provider in a real Agent, reusing the real resolver wiring
// (ResolverSelector). Lets FlowHarness run a flow end to end with a scripted mouth instead of a live CLI.
internal sealed class ScriptedAgentFactory(IProvider provider, ResolverSelector resolvers) : IAgentFactory
{
    public Agent Create(AgentConfig config, string projectDir)
    {
        var (resolver, cap) = resolvers.For(config);
        return new Agent(provider, resolver, cap, projectDir);
    }
}
