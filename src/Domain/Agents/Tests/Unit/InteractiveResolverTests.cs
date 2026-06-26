using ABox.Domain.Agents;

namespace ABox.Agents.Tests.Unit;

public class InteractiveResolverTests
{
    private const string Envelope =
        "Reasoning...\n<<NEEDS_INPUT>>\n{ \"kind\": \"open\", \"prompt\": \"Which bucket?\" }";

    [Rule("Agent emitting NEEDS_INPUT → blocks on a pending decision until a human resolves it, then resumes to Completed")]
    [Fact]
    public async Task Blocks_until_a_human_resolves_then_the_run_resumes()
    {
        var pending = new PendingDecisions();
        var agent = new Agent(new ScriptedProvider(Envelope, "done"), new InteractiveResolver(pending), null, "C:/proj");

        var run = Op.Exec(agent, new AgentArgs("process", "go"));
        var decision = await WaitForPending(pending);
        Assert.Equal("Which bucket?", decision.Prompt);

        Assert.True(pending.Resolve(decision.Id, "use bucket A"));
        var outcome = await run;

        Assert.IsType<AgentOutcome.Completed>(outcome);
    }

    [Rule("Run cancelled while awaiting a decision → unblocks as terminal NeedsInput and drops the pending decision")]
    [Fact]
    public async Task Run_cancel_unblocks_the_await_as_terminal_needs_input()
    {
        var pending = new PendingDecisions();
        var agent = new Agent(new ScriptedProvider(Envelope, "done"), new InteractiveResolver(pending), null, "C:/proj");
        using var cts = new CancellationTokenSource();

        var run = Op.Exec(agent, new AgentArgs("process", "go"), ".", cts.Token);
        await WaitForPending(pending);
        await cts.CancelAsync();

        var outcome = await run;

        Assert.IsType<AgentOutcome.NeedsInput>(outcome);
        Assert.Empty(pending.List());
    }

    private static async Task<PendingDecision> WaitForPending(PendingDecisions pending)
    {
        for (var i = 0; i < 200; i++)
        {
            if (pending.List() is [var first, ..]) return first;
            await Task.Delay(10);
        }
        throw new TimeoutException("no pending decision appeared");
    }
}
