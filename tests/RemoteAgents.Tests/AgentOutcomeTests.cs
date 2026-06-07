using RemoteAgents.Actors.Agents;
using RemoteAgents.Flows;

namespace RemoteAgents.Tests;

public class AgentOutcomeTests
{
    private const string Envelope =
        "Reasoning...\n<<NEEDS_INPUT>>\n{ \"kind\": \"open\", \"prompt\": \"Which bucket?\" }";

    [Fact]
    public async Task An_envelope_in_the_output_becomes_NeedsInput()
    {
        var agent = new Agent(new CannedProvider(Envelope, exit: 0), "C:/proj");

        var outcome = await Op.Exec(agent, new AgentArgs("run", "deploy"));

        var needs = Assert.IsType<AgentOutcome.NeedsInput>(outcome);
        Assert.Equal("Which bucket?", needs.Question.Prompt);
    }

    [Fact]
    public async Task Output_without_an_envelope_is_Completed()
    {
        var agent = new Agent(new CannedProvider("All done.", exit: 0), "C:/proj");

        var outcome = await Op.Exec(agent, new AgentArgs("run", "do it"));

        Assert.Equal("All done.", Assert.IsType<AgentOutcome.Completed>(outcome).Result.Text);
    }

    [Fact]
    public async Task A_nonzero_exit_faults_even_when_an_envelope_is_present()
    {
        // A broken executor emits a valid-looking envelope; Faulted must win.
        var agent = new Agent(new CannedProvider(Envelope, exit: 1), "C:/proj");

        var outcome = await Op.Exec(agent, new AgentArgs("run", "deploy"));

        Assert.Equal(1, Assert.IsType<AgentOutcome.Faulted>(outcome).Error.ExitCode);
    }

    [Fact]
    public async Task A_flow_resolves_a_question_and_resumes_to_completion()
    {
        var agent = new Agent(new TwoTurnProvider(), "C:/proj");
        var flow = new ResolvingFlow(agent, new FixedResolver("s3://acme"), "deploy it");

        await flow.ExecuteAsync(new FlowConfig("f", "t"), new FlowContext("f", "p", "C:/proj", "seed"), CancellationToken.None);

        var done = Assert.IsType<AgentOutcome.Completed>(flow.Final);
        Assert.Contains("s3://acme", done.Result.Text);
    }

    [Fact]
    public async Task The_noninteractive_resolver_leaves_the_question_terminal()
    {
        var agent = new Agent(new TwoTurnProvider(), "C:/proj");
        var flow = new ResolvingFlow(agent, new NonInteractiveResolver(), "deploy it");

        await flow.ExecuteAsync(new FlowConfig("f", "t"), new FlowContext("f", "p", "C:/proj", "seed"), CancellationToken.None);

        Assert.IsType<AgentOutcome.NeedsInput>(flow.Final);
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

    private sealed class FixedResolver(string answer) : IDecisionResolver
    {
        public Task<string?> ResolveAsync(AgentQuestion question, CancellationToken ct)
            => Task.FromResult<string?>(answer);
    }

    private sealed class ResolvingFlow(Agent agent, IDecisionResolver resolver, string prompt) : Flow
    {
        public AgentOutcome Final { get; private set; } = default!;

        protected override async Task RunAsync(FlowConfig config, FlowContext ctx, CancellationToken ct)
        {
            var outcome = await Run(agent, new AgentArgs("turn", prompt), ct);
            while (outcome is AgentOutcome.NeedsInput needs)
            {
                var answer = await resolver.ResolveAsync(needs.Question, ct);
                if (answer is null) break;
                outcome = await Run(agent, new AgentArgs("turn", answer), ct);
            }
            Final = outcome;
        }
    }
}
