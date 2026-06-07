using RemoteAgents.Actors.Agents;
using RemoteAgents.Contracts;
using RemoteAgents.Engine.Flows;

namespace RemoteAgents.Tests;

public class DecisionAuditTests
{
    private const string Envelope =
        "Reasoning...\n<<NEEDS_INPUT>>\n{ \"kind\": \"open\", \"prompt\": \"Which bucket?\" }";

    [Fact]
    public async Task An_auto_resolved_question_is_recorded_on_the_run_ledger()
    {
        var provider = new ScriptedProvider(new Queue<string>(new[] { Envelope, "done" }));
        var agent = new Agent(provider, new AutoResolver(), resolveCap: 8, "C:/proj");
        var flow = new OpFlow<AgentArgs, AgentOutcome>(agent, new AgentArgs("process", "go"));
        var ctx = new FlowContext("audit-flow", "proj", "C:/proj", "go");
        var stream = new SnapshotStream(flow, ctx);

        await flow.ExecuteAsync(new FlowConfig("audit-flow", "t"), ctx, CancellationToken.None);

        var decision = Assert.Single(stream.Latest.Decisions);
        Assert.Equal("Question", decision.Kind);
        Assert.Equal("Which bucket?", decision.Prompt);
        Assert.Equal("Auto", decision.Source);
        Assert.False(string.IsNullOrWhiteSpace(decision.Answer));
    }

    private sealed class ScriptedProvider(Queue<string> replies) : IProvider
    {
        public Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct)
        {
            var text = replies.Dequeue();
            return Task.FromResult(new DriveResult(text, request.SessionId ?? "s1", 0, text, []));
        }
    }
}
