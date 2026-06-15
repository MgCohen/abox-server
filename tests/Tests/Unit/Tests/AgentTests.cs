using ABox.Domain.Flow;
using ABox.Domain.Flow.Operations;
using ABox.Domain.Agents;

namespace ABox.Tests.Unit.Tests;

public class AgentTests
{
    private sealed class FakeAgentFactory : IAgentFactory
    {
        public Agent Create(AgentConfig config, string projectDir) =>
            new(new FakeProvider(config), new NonInteractiveResolver(), null, projectDir);
    }

    [Rule("A factory-minted agent run through a flow → a Completed operation whose summary is the agent's text")]
    [Fact]
    public async Task A_factory_minted_agent_runs_through_the_flow_and_its_text_is_the_summary()
    {
        var agent = new FakeAgentFactory().Create(Agents.Implementer, "C:/proj");
        var flow = new OpFlow<AgentArgs, AgentOutcome>(agent, new AgentArgs("process", "do the thing"));
        var ctx = new FlowContext("agent-flow", "proj", "C:/proj", "seed");
        var stream = new SnapshotStream(flow, ctx);

        await flow.ExecuteAsync(new FlowConfig("agent-flow", "t"), ctx, CancellationToken.None);

        var op = stream.Latest.Operations.Single();
        Assert.Equal("process", op.Name);
        Assert.Equal(OperationStatus.Completed, op.Status);
        Assert.Equal("[implementer] do the thing", op.Summary);
    }

    [Rule("An agent across successive calls → no session on the first call, then the minted session on later calls")]
    [Fact]
    public async Task An_agent_reuses_its_session_across_calls()
    {
        var seen = new List<AgentRunRequest>();
        var agent = new Agent(new CapturingProvider(seen.Add), new NonInteractiveResolver(), null, "C:/proj");

        await Op.Exec(agent, new AgentArgs("first", "one"));
        await Op.Exec(agent, new AgentArgs("second", "two"));

        Assert.Equal(2, seen.Count);
        Assert.Null(seen[0].SessionId);
        Assert.Equal(MintedSession, seen[1].SessionId);
    }

    [Rule("An agent's first call → a request carrying the prompt and baked-in project dir with no session")]
    [Fact]
    public async Task An_agent_bakes_its_project_dir_and_starts_the_first_call_without_a_session()
    {
        AgentRunRequest? seen = null;
        var agent = new Agent(new CapturingProvider(r => seen = r), new NonInteractiveResolver(), null, "C:/work/card");

        await Op.Exec(agent, new AgentArgs("implement", "p"));

        Assert.NotNull(seen);
        Assert.Equal("p", seen!.Prompt);
        Assert.Equal("C:/work/card", seen.ProjectDir);
        Assert.Null(seen.SessionId);
    }

    [Rule("A completed agent outcome → a transcript with the agent's text turn and a session id")]
    [Fact]
    public async Task An_agent_result_carries_the_transcript()
    {
        var agent = new Agent(new FakeProvider(Agents.Reviewer), new NonInteractiveResolver(), null, "C:/proj");

        var outcome = await Op.Exec(agent, new AgentArgs("look", "look"));

        var result = Assert.IsType<AgentOutcome.Completed>(outcome).Result;
        var turn = Assert.Single(result.Transcript);
        Assert.Equal(AgentTurnKind.Text, turn.Kind);
        Assert.Equal("[reviewer] look", turn.Body);
        Assert.False(string.IsNullOrEmpty(result.SessionId));
    }

    [Rule("AgentRunRequest with a blank prompt → ArgumentException")]
    [Fact]
    public void AgentRunRequest_rejects_a_blank_prompt()
    {
        Assert.Throws<ArgumentException>(() => new AgentRunRequest(" ", "C:/proj"));
    }

    private const string MintedSession = "minted-session";

    private sealed class CapturingProvider(Action<AgentRunRequest> capture) : IProvider
    {
        public Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct)
        {
            capture(request);
            return Task.FromResult(new DriveResult("ok", request.SessionId ?? MintedSession, 0, "ok", []));
        }
    }
}
