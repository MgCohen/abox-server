using RemoteAgents.Actors.Agents;
using RemoteAgents.Actors.Agents.Claude;

namespace RemoteAgents.Tests;

// The seam that turns Resolution into wiring: AgentFactory must wire Auto to the
// auto-resolver (capped) and Human to the human resolver (uncapped). The
// always-asking fake forces the resolve loop so the wiring is observable.
public class AgentFactoryTests
{
    private const string Envelope =
        "Reasoning...\n<<NEEDS_INPUT>>\n{ \"kind\": \"open\", \"prompt\": \"Which bucket?\" }";

    [Fact]
    public async Task Auto_is_wired_to_the_auto_resolver_and_capped()
    {
        var human = new SpyResolver(null);
        var factory = new AgentFactory(human, new AutoResolver(), new DenyResolver(), new AutoPolicy());
        var config = new FakeAgentConfig("a", "d", "m", "s", Reply: Envelope)
        {
            Resolution = Resolution.Auto,
            ResolveCap = 2,
        };

        var outcome = await Op.Exec(factory.Create(config, "C:/proj"), new AgentArgs("turn", "go"));

        var faulted = Assert.IsType<AgentOutcome.Faulted>(outcome);
        Assert.Contains("exhausted after 2", faulted.Error.Message);
        Assert.Equal(0, human.Calls);
    }

    [Fact]
    public async Task Human_is_wired_to_the_human_resolver()
    {
        var human = new SpyResolver(null);
        var factory = new AgentFactory(human, new AutoResolver(), new DenyResolver(), new AutoPolicy());
        var config = new FakeAgentConfig("a", "d", "m", "s", Reply: Envelope)
        {
            Resolution = Resolution.Human,
        };

        var outcome = await Op.Exec(factory.Create(config, "C:/proj"), new AgentArgs("turn", "go"));

        Assert.IsType<AgentOutcome.NeedsInput>(outcome);
        Assert.Equal(1, human.Calls);
    }

    [Fact]
    public async Task Deny_is_wired_and_runs_uncapped()
    {
        var human = new SpyResolver(null);
        var factory = new AgentFactory(human, new AutoResolver(), new DenyResolver(), new AutoPolicy());
        var config = new FakeAgentConfig("a", "d", "m", "s", Reply: Envelope)
        {
            Resolution = Resolution.Deny,
        };

        var outcome = await Op.Exec(factory.Create(config, "C:/proj"), new AgentArgs("turn", "go"));

        Assert.IsType<AgentOutcome.NeedsInput>(outcome);
        Assert.Equal(0, human.Calls);
    }

    private sealed class SpyResolver(string? answer) : IDecisionResolver
    {
        public int Calls { get; private set; }

        public Resolution Source => Resolution.Human;

        public Task<string?> ResolveAsync(AgentQuestion question, DecisionKind kind, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(answer);
        }
    }
}
