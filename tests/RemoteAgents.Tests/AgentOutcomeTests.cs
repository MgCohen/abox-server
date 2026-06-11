using RemoteAgents.Domain.Agents;

namespace RemoteAgents.Tests;

public class AgentOutcomeTests
{
    private const string Envelope =
        "Reasoning...\n<<NEEDS_INPUT>>\n{ \"kind\": \"open\", \"prompt\": \"Which bucket?\" }";

    [Fact]
    public async Task An_envelope_in_the_output_becomes_NeedsInput()
    {
        var agent = new Agent(new CannedProvider(Envelope, exit: 0), new NonInteractiveResolver(), null, "C:/proj");

        var outcome = await Op.Exec(agent, new AgentArgs("run", "deploy"));

        var needs = Assert.IsType<AgentOutcome.NeedsInput>(outcome);
        Assert.Equal("Which bucket?", needs.Question.Prompt);
    }

    [Fact]
    public async Task Output_without_an_envelope_is_Completed()
    {
        var agent = new Agent(new CannedProvider("All done.", exit: 0), new NonInteractiveResolver(), null, "C:/proj");

        var outcome = await Op.Exec(agent, new AgentArgs("run", "do it"));

        Assert.Equal("All done.", Assert.IsType<AgentOutcome.Completed>(outcome).Result.Text);
    }

    [Fact]
    public async Task A_nonzero_exit_faults_even_when_an_envelope_is_present()
    {
        // A broken executor emits a valid-looking envelope; Faulted must win.
        var agent = new Agent(new CannedProvider(Envelope, exit: 1), new NonInteractiveResolver(), null, "C:/proj");

        var outcome = await Op.Exec(agent, new AgentArgs("run", "deploy"));

        Assert.Equal(1, Assert.IsType<AgentOutcome.Faulted>(outcome).Error.ExitCode);
    }

    [Fact]
    public async Task The_agent_resolves_a_question_and_resumes_to_completion()
    {
        var agent = new Agent(new TwoTurnProvider(), new FixedResolver("s3://acme"), 8, "C:/proj");

        var outcome = await Op.Exec(agent, new AgentArgs("turn", "deploy it"));

        var done = Assert.IsType<AgentOutcome.Completed>(outcome);
        Assert.Contains("s3://acme", done.Result.Text);
    }

    [Fact]
    public async Task A_null_answer_leaves_the_question_terminal()
    {
        var agent = new Agent(new TwoTurnProvider(), new NonInteractiveResolver(), 8, "C:/proj");

        var outcome = await Op.Exec(agent, new AgentArgs("turn", "deploy it"));

        Assert.IsType<AgentOutcome.NeedsInput>(outcome);
    }

    [Fact]
    public async Task The_cap_faults_a_question_loop_that_never_settles()
    {
        var agent = new Agent(new AlwaysAsksProvider(), new FixedResolver("more"), 3, "C:/proj");

        var outcome = await Op.Exec(agent, new AgentArgs("turn", "go"));

        var faulted = Assert.IsType<AgentOutcome.Faulted>(outcome);
        Assert.Contains("exhausted", faulted.Error.Message);
    }

    private sealed class CannedProvider(string text, int exit) : IProvider
    {
        public Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct)
            => Task.FromResult(new DriveResult(text, "sid", exit, text, []));
    }

    // Turn 1 (no session) asks; turn 2 (resumed) completes using the answer prompt.
    private sealed class TwoTurnProvider : IProvider
    {
        public Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct)
        {
            var text = request.SessionId is null ? Envelope : $"Done, deployed to {request.Prompt}.";
            return Task.FromResult(new DriveResult(text, "s1", 0, text, []));
        }
    }

    private sealed class AlwaysAsksProvider : IProvider
    {
        public Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct)
            => Task.FromResult(new DriveResult(Envelope, "s1", 0, Envelope, []));
    }

    private sealed class FixedResolver(string answer) : IDecisionResolver
    {
        public Resolution Source => Resolution.Human;

        public Task<string?> ResolveAsync(AgentQuestion question, DecisionKind kind, CancellationToken ct)
            => Task.FromResult<string?>(answer);
    }
}
