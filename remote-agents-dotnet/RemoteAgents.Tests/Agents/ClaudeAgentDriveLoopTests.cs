using RemoteAgents.Agents;
using RemoteAgents.Events;

namespace RemoteAgents.Tests.Agents;

// End-to-end-ish tests for ClaudeAgent.ExecuteAsync that swap the real
// PTY for a FakePtyConnection via the SpawnPtyAsync seam.
//
// These exercise the drive loop's:
//   - startup-dialog detection + dismissal
//   - idle-based completion detection
//   - exit-code semantics (happy path vs Kill teardown)
//   - JSONL-vs-buffer text extraction priority
public class ClaudeAgentDriveLoopTests : IDisposable
{
    private readonly string _projectDir;
    private readonly List<string> _jsonlsToClean = new();

    public ClaudeAgentDriveLoopTests()
    {
        _projectDir = Path.Combine(Path.GetTempPath(), "ra-claude-fake-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_projectDir, recursive: true); } catch { }
        foreach (var p in _jsonlsToClean)
        {
            try { File.Delete(p); } catch { }
            try { Directory.Delete(Path.GetDirectoryName(p)!); } catch { }
        }
    }

    // Compressed dwell windows so the drive loop completes in single-digit
    // seconds. The real defaults (2000/6000/1500/300000ms) are tuned for
    // a human-paced Claude UI.
    private static ClaudeAgentOptions FastOpts() => new(
        InitialDwellMs: 50,
        IdleThresholdMs: 200,
        ExitDwellMs: 50,
        MaxWaitMs: 5_000);

    [Fact]
    public async Task happy_path_returns_jsonl_text_and_exit_code_zero()
    {
        var sessionId = Guid.NewGuid().ToString();
        StageClaudeJsonl(sessionId,
            UserLine("Summarize the code."),
            AssistantTextLine("Here is the summary."));

        var fake = new FakePtyConnection();
        // Wire: when the agent writes "exit\r", flip the PTY to exited
        // so WaitForExit returns true.
        fake.OnWriteText = w =>
        {
            if (w.Contains("exit\r")) fake.Exit(0);
        };
        // Provide some scripted reader output so the idle-completion
        // detector fires (lastChunkAt advances).
        fake.EnqueueRead("Hi from claude.\n");

        var agent = new TestableClaudeAgent(fake) { Name = "claude", Options = FastOpts() };
        var result = await agent.RunAsync(new AgentRunRequest(
            Prompt: "Summarize the code.",
            SessionId: sessionId,
            ProjectDir: _projectDir));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Here is the summary.", result.Text);
        // Sanity: launch line, prompt, /exit, exit should all be present.
        var captured = fake.Captured.ToString();
        Assert.Contains("claude ", captured);
        Assert.Contains("Summarize the code.", captured);
        Assert.Contains("/exit", captured);
    }

    [Fact]
    public async Task trust_dialog_is_dismissed_with_enter()
    {
        var sessionId = Guid.NewGuid().ToString();
        StageClaudeJsonl(sessionId,
            UserLine("hi"),
            AssistantTextLine("hi there"));

        var fake = new FakePtyConnection();
        // The agent inspects the buffer after InitialDwellMs and looks
        // for the trust-dialog text. Enqueue it before launch.
        fake.EnqueueRead("Do you trust this folder?\n");
        fake.OnWriteText = w => { if (w.Contains("exit\r")) fake.Exit(0); };

        AgentEvent.DialogDismissed? dismissed = null;
        var sink = new CapturingSink(evt =>
        {
            if (evt is AgentEvent.DialogDismissed d) dismissed = d;
        });

        var agent = new TestableClaudeAgent(fake) { Name = "claude", Sink = sink, Options = FastOpts() };
        await agent.RunAsync(new AgentRunRequest("hi", sessionId, _projectDir));

        Assert.NotNull(dismissed);
        Assert.Equal("trust", dismissed!.Match);
    }

    [Fact]
    public async Task abnormal_teardown_returns_exit_code_minus_one()
    {
        var sessionId = Guid.NewGuid().ToString();
        // No JSONL staged → text extraction falls back to buffer.

        var fake = new FakePtyConnection();
        // Never exit. WaitForExit will time out, Kill() will be called,
        // ExecuteAsync should surface -1.
        fake.EnqueueRead("some output\n");

        var opts = new ClaudeAgentOptions(
            InitialDwellMs: 10,
            IdleThresholdMs: 50,
            ExitDwellMs: 10,
            MaxWaitMs: 500,
            // WaitForExit timeout shrunk so this test doesn't pay 15s.
            WaitForExitMs: 200,
            ReaderDrainMs: 100);

        var agent = new TestableClaudeAgent(fake) { Name = "claude", Options = opts };
        var result = await agent.RunAsync(new AgentRunRequest("hello", sessionId, _projectDir));

        Assert.Equal(-1, result.ExitCode);
    }

    [Fact]
    public async Task jsonl_text_preferred_over_buffer_when_both_available()
    {
        var sessionId = Guid.NewGuid().ToString();
        StageClaudeJsonl(sessionId,
            UserLine("anything"),
            AssistantTextLine("REAL_TEXT_FROM_JSONL"));

        var fake = new FakePtyConnection();
        fake.OnWriteText = w => { if (w.Contains("exit\r")) fake.Exit(0); };
        // Stuff the buffer with something resembling Claude's prompt-echo +
        // a different reply. If the JSONL is preferred, we should NOT see
        // this text in result.Text.
        fake.EnqueueRead("anything\nFAKE_BUFFER_REPLY\n");

        var agent = new TestableClaudeAgent(fake) { Name = "claude", Options = FastOpts() };
        var result = await agent.RunAsync(new AgentRunRequest("anything", sessionId, _projectDir));

        Assert.Equal("REAL_TEXT_FROM_JSONL", result.Text);
        Assert.DoesNotContain("FAKE_BUFFER_REPLY", result.Text);
    }

    // ── helpers ────────────────────────────────────────────────────────

    private void StageClaudeJsonl(string sessionId, params string[] lines)
    {
        var path = ClaudeJsonl.PathFor(_projectDir, sessionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
        _jsonlsToClean.Add(path);
    }

    private static string UserLine(string text)
        => $"{{\"type\":\"user\",\"message\":{{\"role\":\"user\",\"content\":[{{\"type\":\"text\",\"text\":{Q(text)}}}]}}}}";

    private static string AssistantTextLine(string text)
        => $"{{\"type\":\"assistant\",\"message\":{{\"role\":\"assistant\",\"content\":[{{\"type\":\"text\",\"text\":{Q(text)}}}]}}}}";

    private static string Q(string s)
        => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private sealed class CapturingSink : IEventSink
    {
        private readonly Action<AgentEvent> _handler;
        public CapturingSink(Action<AgentEvent> handler) { _handler = handler; }
        public Task EmitAsync(AgentEvent evt, CancellationToken ct = default)
        {
            _handler(evt);
            return Task.CompletedTask;
        }
    }
}
