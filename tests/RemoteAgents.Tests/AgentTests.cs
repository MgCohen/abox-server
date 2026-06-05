using RemoteAgents.Contracts;
using RemoteAgents.Flows;
using RemoteAgents.Actors.Agents;

namespace RemoteAgents.Tests;

public class AgentTests
{
    private sealed class FakeAgentFactory : IAgentFactory
    {
        public Agent Create(AgentConfig config, string projectDir) => new(new FakeProvider(config), projectDir);
    }

    [Fact]
    public async Task A_factory_minted_agent_runs_through_the_flow_and_its_text_is_the_summary()
    {
        var agent = new FakeAgentFactory().Create(Agents.Implementer, "C:/proj");
        var flow = new OpFlow<AgentArgs, AgentResult>(agent, new AgentArgs("process", "do the thing"));
        var ctx = new FlowContext("agent-flow", "proj", "C:/proj", "seed");
        var stream = new SnapshotStream(flow, ctx);

        await flow.ExecuteAsync(new FlowConfig("agent-flow", "t"), ctx, CancellationToken.None);

        var op = stream.Latest.Operations.Single();
        Assert.Equal("process", op.Name);
        Assert.Equal(OperationStatus.Completed, op.Status);
        Assert.Equal("[implementer] do the thing", op.Summary);
    }

    [Fact]
    public async Task An_agent_reuses_its_session_across_calls()
    {
        var seen = new List<AgentRunRequest>();
        var agent = new Agent(new CapturingProvider(seen.Add), "C:/proj");

        await Op.Exec(agent, new AgentArgs("first", "one"));
        await Op.Exec(agent, new AgentArgs("second", "two"));

        Assert.Equal(2, seen.Count);
        Assert.False(string.IsNullOrEmpty(seen[0].SessionId));
        Assert.Equal(seen[0].SessionId, seen[1].SessionId);
    }

    [Fact]
    public async Task An_agent_bakes_its_project_dir_and_threads_a_session_into_the_request()
    {
        AgentRunRequest? seen = null;
        var agent = new Agent(new CapturingProvider(r => seen = r), "C:/work/card");

        await Op.Exec(agent, new AgentArgs("implement", "p"));

        Assert.NotNull(seen);
        Assert.Equal("p", seen!.Prompt);
        Assert.Equal("C:/work/card", seen.ProjectDir);
        Assert.False(string.IsNullOrEmpty(seen.SessionId));
    }

    [Fact]
    public async Task An_agent_result_carries_the_transcript()
    {
        var agent = new Agent(new FakeProvider(Agents.Reviewer), "C:/proj");

        var result = await Op.Exec(agent, new AgentArgs("look", "look"));

        var turn = Assert.Single(result.Transcript);
        Assert.Equal(AgentTurnKind.Text, turn.Kind);
        Assert.Equal("[reviewer] look", turn.Body);
        Assert.False(string.IsNullOrEmpty(result.SessionId));
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
            return Task.FromResult(new DriveResult("ok", request.SessionId ?? "s", 0, "ok", []));
        }
    }
}
