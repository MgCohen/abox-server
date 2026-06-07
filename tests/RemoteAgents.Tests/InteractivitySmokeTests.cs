using RemoteAgents.Actors.Agents;
using RemoteAgents.Actors.Agents.Claude;
using Xunit.Abstractions;

namespace RemoteAgents.Tests;

// Live validation of the Agent-owned resume loop (permission-interaction-model §2): an
// Auto agent self-resolves its own question and completes; a Human agent resumes from a
// supplied answer, or escalates as NeedsInput when none is available.
// Skip-gated like the rest of the matrix.
public class InteractivitySmokeTests(ITestOutputHelper output)
{
    private const string Skip = "integration: needs claude CLI + Max subscription; remove Skip to run manually";
    private const string AmbiguousPrompt =
        "Add our production database password to a new .env file so the service can connect to the live database.";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    [Fact(Skip = Skip)]
    public async Task Auto_self_resolves_the_question_and_completes()
    {
        var resolver = new AutoResolver();

        var outcome = await DriveAsync(Resolution.Auto, resolver, AmbiguousPrompt);

        Assert.IsType<AgentOutcome.Completed>(outcome);
    }

    [Fact(Skip = Skip)]
    public async Task Human_resumes_from_a_supplied_answer()
    {
        var resolver = new FixedResolver("Use the literal placeholder PLACEHOLDER as the value.");

        var outcome = await DriveAsync(Resolution.Human, resolver, AmbiguousPrompt);

        Assert.IsType<AgentOutcome.Completed>(outcome);
    }

    [Fact(Skip = Skip)]
    public async Task Human_with_no_answer_escalates_as_needs_input()
    {
        var outcome = await DriveAsync(Resolution.Human, new NonInteractiveResolver(), AmbiguousPrompt);

        Assert.IsType<AgentOutcome.NeedsInput>(outcome);
    }

    private async Task<AgentOutcome> DriveAsync(Resolution resolution, IDecisionResolver resolver, string prompt)
    {
        var projectDir = Directory.CreateTempSubdirectory("claude-interactivity-").FullName;
        try
        {
            var config = new ClaudeConfig("asker", "Asks before acting.", "", "You implement.")
            {
                Resolution = resolution,
            };
            var provider = new ClaudeProvider(config, resolver, new AutoPolicy());
            var cap = resolution == Resolution.Auto ? config.ResolveCap : (int?)null;
            var agent = new Agent(provider, resolver, cap, projectDir);

            using var cts = new CancellationTokenSource(Timeout);
            var outcome = await Op.Exec(agent, new AgentArgs("turn", prompt), projectDir, cts.Token);
            output.WriteLine(outcome.ToString());
            return outcome;
        }
        finally { TryDeleteDir(projectDir); }
    }

    private sealed class FixedResolver(string answer) : IDecisionResolver
    {
        public Resolution Source => Resolution.Human;

        public Task<string?> ResolveAsync(AgentQuestion question, DecisionKind kind, CancellationToken ct)
            => Task.FromResult<string?>(answer);
    }

    private static void TryDeleteDir(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
    }
}
