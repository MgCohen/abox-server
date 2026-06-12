using RemoteAgents.Domain.Agents;
using RemoteAgents.Domain.Agents.Claude;
using Xunit.Abstractions;

namespace RemoteAgents.Tests.Live.Tests;

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

    // Exercises the real registry: InteractiveResolver parks the question and a
    // background fulfiller (standing in for the inbox/endpoint) Resolves it, so the
    // run resumes against a live CLI.
    [Fact(Skip = Skip)]
    public async Task Human_resumes_from_a_registry_answer()
    {
        var pending = new PendingDecisions();
        var resolver = new InteractiveResolver(pending);

        using var fulfillerCts = new CancellationTokenSource();
        var fulfiller = Task.Run(async () =>
        {
            while (!fulfillerCts.IsCancellationRequested)
            {
                foreach (var d in pending.List())
                    pending.Resolve(d.Id, "Use the literal placeholder PLACEHOLDER as the value.");
                try { await Task.Delay(50, fulfillerCts.Token); } catch (OperationCanceledException) { return; }
            }
        });

        var outcome = await DriveAsync(Resolution.Human, resolver, AmbiguousPrompt);
        await fulfillerCts.CancelAsync();
        await fulfiller;

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

    private static void TryDeleteDir(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
    }
}
