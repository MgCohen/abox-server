using RemoteAgents.Actors.Agents;
using RemoteAgents.Actors.Agents.Claude;

namespace RemoteAgents.Tests;

// The seam that combines the two axes: AgentFactory must wire Autonomous to the
// auto-resolver (capped) and Interactive to the human resolver (uncapped). The
// always-asking fake forces the resolve loop so the wiring is observable.
public class AgentFactoryTests
{
    private const string Envelope =
        "Reasoning...\n<<NEEDS_INPUT>>\n{ \"kind\": \"open\", \"prompt\": \"Which bucket?\" }";

    [Fact]
    public async Task Autonomous_is_wired_to_the_auto_resolver_and_capped()
    {
        var human = new SpyResolver(null);
        var factory = new AgentFactory(human, new AutoResolver(), new AutoPolicy());
        var config = new FakeAgentConfig("a", "d", "m", "s", Reply: Envelope)
        {
            Interactivity = Interactivity.Autonomous,
            ResolveCap = 2,
        };

        var outcome = await Op.Exec(factory.Create(config, "C:/proj"), new AgentArgs("turn", "go"));

        var faulted = Assert.IsType<AgentOutcome.Faulted>(outcome);
        Assert.Contains("exhausted after 2", faulted.Error.Message);
        Assert.Equal(0, human.Calls);
    }

    [Fact]
    public async Task Interactive_is_wired_to_the_human_resolver()
    {
        var human = new SpyResolver(null);
        var factory = new AgentFactory(human, new AutoResolver(), new AutoPolicy());
        var config = new FakeAgentConfig("a", "d", "m", "s", Reply: Envelope)
        {
            Interactivity = Interactivity.Interactive,
        };

        var outcome = await Op.Exec(factory.Create(config, "C:/proj"), new AgentArgs("turn", "go"));

        Assert.IsType<AgentOutcome.NeedsInput>(outcome);
        Assert.Equal(1, human.Calls);
    }

    private sealed class SpyResolver(string? answer) : IDecisionResolver
    {
        public int Calls { get; private set; }

        public Task<string?> ResolveAsync(AgentQuestion question, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(answer);
        }
    }
}
