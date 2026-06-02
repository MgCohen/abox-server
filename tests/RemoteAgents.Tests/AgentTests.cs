using RemoteAgents.Contracts;
using RemoteAgents.Flows;
using RemoteAgents.Actors.Agents;

namespace RemoteAgents.Tests;

public class AgentTests
{
    private sealed class FakeAgentFactory : IAgentFactory
    {
        public Agent Create(AgentConfig config) => new FakeAgent(config);
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
        var agent = new FakeAgent(Agents.Implementer);

        var first = await agent.Run("one").Execute(new FlowContext("f", "p", "C:/proj", "seed"), CancellationToken.None);
        var second = await agent.Run("two").Execute(new FlowContext("f", "p", "C:/proj", "seed"), CancellationToken.None);

        Assert.Equal("[implementer] one", first.Text);
        Assert.Equal("[implementer] two", second.Text);
    }

    [Fact]
    public async Task An_agent_operation_builds_its_request_from_config_and_run_context()
    {
        AgentRunRequest? seen = null;
        var config = new FakeAgentConfig("capture", "d", "the-model", "the-system-prompt");
        var flow = new CapturingFlow(config, req => seen = req);

        await flow.ExecuteAsync(new FlowConfig("cap", "t"), new FlowContext("cap", "proj", "C:/work/card", "seed"), CancellationToken.None);

        Assert.NotNull(seen);
        Assert.Equal("C:/work/card", seen!.ProjectDir);
        Assert.Equal("p", seen.Prompt);
        Assert.Equal("the-model", seen.Model);
        Assert.Equal("the-system-prompt", seen.SystemPrompt);
    }

    [Fact]
    public async Task An_agent_result_carries_the_transcript()
    {
        var op = new FakeAgent(Agents.Reviewer).Run("look");

        var result = await op.Execute(new FlowContext("f", "proj", "C:/proj", "seed"), CancellationToken.None);

        var turn = Assert.Single(result.Transcript);
        Assert.Equal(AgentTurnKind.Text, turn.Kind);
        Assert.Equal("[reviewer] look", turn.Body);
        Assert.Equal("fake-session", result.SessionId);
    }

    [Fact]
    public void AgentRunRequest_rejects_a_blank_prompt()
    {
        Assert.Throws<ArgumentException>(() => new AgentRunRequest(" ", "C:/proj", "m", "p"));
    }

    private sealed class CapturingFlow(AgentConfig config, Action<AgentRunRequest> capture) : Flow
    {
        protected override Task RunAsync(FlowConfig flowConfig, FlowContext ctx, CancellationToken ct) =>
            Run(new CapturingAgent(config, capture).Run("p"), ct);
    }

    private sealed class CapturingAgent(AgentConfig config, Action<AgentRunRequest> capture) : Agent(config)
    {
        protected override Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct)
        {
            capture(request);
            return Task.FromResult(new DriveResult("ok", "s", 0, "ok", []));
        }
    }
}
