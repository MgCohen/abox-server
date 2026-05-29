using RemoteAgents.Agents;
using RemoteAgents.Events;

namespace RemoteAgents.Tests.Agents;

// Captures every event emitted so tests can assert on the full sequence.
file sealed class CaptureSink : IEventSink
{
    public List<AgentEvent> Events { get; } = new();
    public Task EmitAsync(AgentEvent evt, CancellationToken ct = default)
    {
        Events.Add(evt);
        return Task.CompletedTask;
    }
}

// Runs to completion, emitting a couple of stream chunks + a dialog dismissal.
file sealed class HappyAgent : Agent
{
    protected override async Task<AgentResult> ExecuteAsync(AgentRunRequest req, CancellationToken ct)
    {
        await Sink.EmitAsync(new AgentEvent.StreamChunk(DateTimeOffset.UtcNow, Name, "hello"), ct);
        await Sink.EmitAsync(new AgentEvent.DialogDismissed(DateTimeOffset.UtcNow, Name, "trust this folder"), ct);
        await Sink.EmitAsync(new AgentEvent.StreamChunk(DateTimeOffset.UtcNow, Name, " world"), ct);
        return new AgentResult(
            Text: "hello world",
            SessionId: "sess-123",
            ExitCode: 0,
            RawOutput: "hello world");
    }
}

// Throws inside ExecuteAsync.
file sealed class SadAgent : Agent
{
    protected override Task<AgentResult> ExecuteAsync(AgentRunRequest req, CancellationToken ct)
        => throw new InvalidOperationException("spawn failed");
}

public class AgentLifecycleTests
{
    private static AgentRunRequest Req() => new("do the thing", null, "C:/some/proj");

    [Fact]
    public async Task Happy_path_emits_started_chunks_dialog_completed_in_order()
    {
        var sink = new CaptureSink();
        var agent = new HappyAgent { Name = "planner", Sink = sink };

        var result = await agent.RunAsync(Req());

        Assert.Equal("hello world", result.Text);
        Assert.Equal("sess-123", result.SessionId);

        Assert.Collection(sink.Events,
            e => Assert.IsType<AgentEvent.Started>(e),
            e => Assert.IsType<AgentEvent.StreamChunk>(e),
            e => Assert.IsType<AgentEvent.DialogDismissed>(e),
            e => Assert.IsType<AgentEvent.StreamChunk>(e),
            e => Assert.IsType<AgentEvent.Completed>(e));

        var completed = (AgentEvent.Completed)sink.Events.Last();
        Assert.Equal("planner", completed.AgentName);
        Assert.Equal("sess-123", completed.SessionId);
        Assert.Equal(0, completed.ExitCode);
        Assert.Equal(11, completed.OutputChars);
    }

    [Fact]
    public async Task Failure_emits_started_then_failed_and_rethrows()
    {
        var sink = new CaptureSink();
        var agent = new SadAgent { Name = "planner", Sink = sink };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.RunAsync(Req()));

        Assert.Equal("spawn failed", ex.Message);
        Assert.Collection(sink.Events,
            e => Assert.IsType<AgentEvent.Started>(e),
            e => Assert.IsType<AgentEvent.Failed>(e));

        var failed = (AgentEvent.Failed)sink.Events.Last();
        Assert.Equal("spawn failed", failed.Reason);
        Assert.Equal("InvalidOperationException", failed.ExceptionType);
    }

    [Fact]
    public async Task Started_event_carries_prompt_and_session_id_from_request()
    {
        var sink = new CaptureSink();
        var agent = new HappyAgent { Name = "planner", Sink = sink };

        await agent.RunAsync(new AgentRunRequest("do x", "resume-id", "C:/some/proj"));

        var started = (AgentEvent.Started)sink.Events.First();
        Assert.Equal("do x", started.Prompt);
        Assert.Equal("resume-id", started.SessionId);
        Assert.Equal("planner", started.AgentName);
    }

    [Fact]
    public async Task Failed_event_fires_even_if_caller_token_is_canceled()
    {
        var sink = new CaptureSink();
        var agent = new SadAgent { Name = "planner", Sink = sink };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await agent.RunAsync(Req(), cts.Token));

        // Failed must still land even with caller's CT canceled — uses CT.None.
        Assert.Contains(sink.Events, e => e is AgentEvent.Failed);
    }

    [Fact]
    public void AgentEvent_variants_are_the_expected_set()
    {
        var t = typeof(AgentEvent);
        var nested = t.GetNestedTypes().Where(nt => nt.IsSubclassOf(t)).Select(nt => nt.Name).ToHashSet();
        Assert.Equal(
            new HashSet<string> { "Started", "StreamChunk", "DialogDismissed", "Completed", "Failed", "Phase", "NonInteractiveViolation", "ProviderSessionAttached" },
            nested);
    }
}
