using RemoteAgents.Contracts;
using RemoteAgents.Flows;
using RemoteAgents.Actors.Agents;

namespace RemoteAgents.Tests;

public class AgentTests
{
    private static Agent Fake(AgentConfig config) => new(config, new FakeProvider(config));

    private sealed class FakeAgentFactory : IAgentFactory
    {
        public Agent Create(AgentConfig config) => Fake(config);
    }

    private sealed class OneAgentFlow(IAgentFactory agents, AgentConfig config, string prompt) : Flow
    {
        protected override Task RunAsync(FlowConfig flowConfig, FlowContext ctx, CancellationToken ct) =>
            Run(agents.Create(config).Run(prompt), ct);
    }

    [Fact]
    public async Task Factory_minted_agent_runs_through_the_flow_and_its_text_is_the_summary()
    {
        var flow = new OneAgentFlow(new FakeAgentFactory(), Agents.Implementer, "do the thing");
        var ctx = new FlowContext("agent-flow", "proj", "C:/proj", "seed");
        var stream = new SnapshotStream(flow, ctx);

        await flow.ExecuteAsync(new FlowConfig("agent-flow", "t"), ctx, CancellationToken.None);

        var op = stream.Latest.Operations.Single();
        Assert.Equal("implementer", op.Name);
        Assert.Equal(OperationStatus.Completed, op.Status);
        Assert.Equal("[implementer] do the thing", op.Summary);
    }

    [Fact]
    public async Task An_agent_is_reusable_minting_a_fresh_operation_per_call()
    {
        var agent = Fake(Agents.Implementer);

        var first = await agent.Run("one").Execute(new FlowContext("f", "p", "C:/proj", "seed"), CancellationToken.None);
        var second = await agent.Run("two").Execute(new FlowContext("f", "p", "C:/proj", "seed"), CancellationToken.None);

        Assert.Equal("[implementer] one", first.Text);
        Assert.Equal("[implementer] two", second.Text);
    }

    [Fact]
    public async Task An_agent_operation_builds_its_request_from_the_run_context()
    {
        AgentRunRequest? seen = null;
        var agent = new Agent(Agents.Implementer, new CapturingProvider(req => seen = req));

        await agent.Run("p", "sess").Execute(new FlowContext("cap", "proj", "C:/work/card", "seed"), CancellationToken.None);

        Assert.NotNull(seen);
        Assert.Equal("p", seen!.Prompt);
        Assert.Equal("C:/work/card", seen.ProjectDir);
        Assert.Equal("sess", seen.SessionId);
    }

    [Fact]
    public async Task An_agent_result_carries_the_transcript()
    {
        var op = Fake(Agents.Reviewer).Run("look");

        var result = await op.Execute(new FlowContext("f", "proj", "C:/proj", "seed"), CancellationToken.None);

        var turn = Assert.Single(result.Transcript);
        Assert.Equal(AgentTurnKind.Text, turn.Kind);
        Assert.Equal("[reviewer] look", turn.Body);
        Assert.Equal("fake-session", result.SessionId);
    }

    [Fact]
    public void AgentRunRequest_rejects_a_blank_prompt()
    {
        Assert.Throws<ArgumentException>(() => new AgentRunRequest(" ", "C:/proj"));
    }

    private sealed class CapturingProvider(Action<AgentRunRequest> capture) : IProvider
    {
        public Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct)
        {
            capture(request);
            return Task.FromResult(new DriveResult("ok", "s", 0, "ok", []));
        }
    }
}
