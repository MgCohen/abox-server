using System.Text.Json;
using RemoteAgents.Events;
using RemoteAgents.Sessions;

namespace RemoteAgents.Tests.Sessions;

public class SessionAndSinksTests : IDisposable
{
    private readonly string _root;

    public SessionAndSinksTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ra-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task End_to_end_flow_writes_session_dir_and_deterministic_transcript()
    {
        // Hand-rolled flow:
        // 1. start session
        // 2. emit Started -> StreamChunk x2 -> Completed via a CompositeSink
        // 3. end session
        var session = Session.Start(new StartSessionRequest(
            ProjectDir: "C:/some/proj",
            ProjectName: "card-framework",
            UserPrompt: "do the thing",
            FlowName: "test-flow"), sessionsRoot: _root);

        var jsonl = new JsonlSink(session.TranscriptFile);
        var sink = new CompositeSink(jsonl);  // console omitted to keep test output clean

        var t0 = DateTimeOffset.UtcNow;
        await sink.EmitAsync(new AgentEvent.Started(t0, "planner", "do the thing", null));
        await sink.EmitAsync(new AgentEvent.StreamChunk(t0.AddMilliseconds(100), "planner", "step 1\n"));
        await sink.EmitAsync(new AgentEvent.StreamChunk(t0.AddMilliseconds(200), "planner", "step 2\n"));
        await sink.EmitAsync(new AgentEvent.Completed(t0.AddMilliseconds(300), "planner", "abc-123", 0, 14));

        session.End("ok");

        // Directory shape
        Assert.True(File.Exists(session.PromptFile));
        Assert.True(File.Exists(session.TranscriptFile));
        Assert.True(File.Exists(session.MetaFile));
        Assert.Equal("do the thing", File.ReadAllText(session.PromptFile));

        // Meta
        var meta = JsonSerializer.Deserialize<SessionMeta>(File.ReadAllText(session.MetaFile))!;
        Assert.Equal("csharp", meta.Orchestrator);
        Assert.Equal("1", meta.SchemaVersion);
        Assert.Equal("test-flow", meta.FlowName);
        Assert.Equal("card-framework", meta.ProjectName);
        Assert.Equal("ok", meta.Result);
        Assert.NotNull(meta.EndedAt);
        Assert.NotNull(meta.DurationMs);

        // Transcript deterministic ordering
        var lines = File.ReadAllLines(session.TranscriptFile);
        Assert.Equal(4, lines.Length);

        var kinds = lines.Select(l => JsonDocument.Parse(l).RootElement.GetProperty("kind").GetString()).ToArray();
        Assert.Equal(new[] { "Started", "StreamChunk", "StreamChunk", "Completed" }, kinds);

        // Each entry carries an At + AgentName
        foreach (var l in lines)
        {
            using var doc = JsonDocument.Parse(l);
            Assert.True(doc.RootElement.TryGetProperty("At", out _));
            Assert.Equal("planner", doc.RootElement.GetProperty("AgentName").GetString());
        }
    }

    [Fact]
    public async Task Failed_session_records_failure_reason()
    {
        var session = Session.Start(new StartSessionRequest(
            null, null, null, "broken"), sessionsRoot: _root);
        var sink = new JsonlSink(session.TranscriptFile);
        await sink.EmitAsync(new AgentEvent.Failed(DateTimeOffset.UtcNow, "planner", "spawn failed", "InvalidOperationException"));
        session.End("failed", failureReason: "spawn failed");

        var meta = JsonSerializer.Deserialize<SessionMeta>(File.ReadAllText(session.MetaFile))!;
        Assert.Equal("failed", meta.Result);
        Assert.Equal("spawn failed", meta.FailureReason);
    }
}
