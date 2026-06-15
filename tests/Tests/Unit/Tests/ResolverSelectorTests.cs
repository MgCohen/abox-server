using ABox.Domain.Agents;

namespace ABox.Tests.Unit.Tests;

// The factory's real decision: map a config's Resolution to the resolver that answers its questions
// plus the loop cap. Auto runs capped (it always answers, could loop); Deny/Human self-terminate uncapped.
// The resolve-loop behaviour itself (cap exhaustion, NeedsInput escalation) is covered by AgentOutcomeTests.
public class ResolverSelectorTests
{
    private static FakeAgentConfig Config(Resolution resolution, int resolveCap = 5) =>
        new("a", "d", "m", "s") { Resolution = resolution, ResolveCap = resolveCap };

    [Rule("ResolverSelector given Auto → the auto-resolver with the configured cap")]
    [Fact]
    public void Auto_maps_to_the_auto_resolver_capped()
    {
        var auto = new AutoResolver();
        var selector = new ResolverSelector(new SpyResolver(), auto, new DenyResolver());

        var (resolver, cap) = selector.For(Config(Resolution.Auto, resolveCap: 2));

        Assert.Same(auto, resolver);
        Assert.Equal(2, cap);
    }

    [Rule("ResolverSelector given Human → the human resolver, uncapped")]
    [Fact]
    public void Human_maps_to_the_human_resolver_uncapped()
    {
        var human = new SpyResolver();
        var selector = new ResolverSelector(human, new AutoResolver(), new DenyResolver());

        var (resolver, cap) = selector.For(Config(Resolution.Human));

        Assert.Same(human, resolver);
        Assert.Null(cap);
    }

    [Rule("ResolverSelector given Deny → the deny resolver, uncapped")]
    [Fact]
    public void Deny_maps_to_the_deny_resolver_uncapped()
    {
        var deny = new DenyResolver();
        var selector = new ResolverSelector(new SpyResolver(), new AutoResolver(), deny);

        var (resolver, cap) = selector.For(Config(Resolution.Deny));

        Assert.Same(deny, resolver);
        Assert.Null(cap);
    }

    private sealed class SpyResolver : IDecisionResolver
    {
        public Resolution Source => Resolution.Human;

        public Task<string?> ResolveAsync(AgentQuestion question, DecisionKind kind, CancellationToken ct) =>
            Task.FromResult<string?>(null);
    }
}
