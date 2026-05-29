using RemoteAgents.Agents;
using RemoteAgents.Events;

namespace RemoteAgents.Tests.Agents;

// Drive-loop tests for ClaudeAgent.ExecuteAsync with HookIntegrationOptions
// attached. Pre-stages hooks.jsonl with canned lines (the real shim isn't
// invoked) and asserts the agent's Status / Question reflect what the
// parser sees.
//
// Install/uninstall are verified by checking that .claude/settings.json
// exists between Install and Uninstall — we hook the in-flight buffer
// callback to peek mid-run.
public class ClaudeAgentHookIntegrationTests : IDisposable
{
    private readonly string _projectDir;
    private readonly string _hooksPath;
    private readonly string _fakeShim;

    public ClaudeAgentHookIntegrationTests()
    {
        _projectDir = Path.Combine(Path.GetTempPath(), "ra-claude-hooks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_projectDir);
        _hooksPath = Path.Combine(_projectDir, "hooks.jsonl");
        _fakeShim  = Path.Combine(_projectDir, "hookshim.ps1");
    }

    public void Dispose()
    {
        try { Directory.Delete(_projectDir, recursive: true); } catch { }
    }

    private static ClaudeAgentOptions FastOpts(HookIntegrationOptions? hooks = null) => new(
        InitialDwellMs: 50,
        IdleThresholdMs: 200,
        ExitDwellMs: 50,
        MaxWaitMs: 5_000,
        Hooks: hooks);

    [Fact]
    public async Task Interactive_mode_with_idle_prompt_in_hooks_sets_needs_input()
    {
        WriteHook("claude.idle_prompt", """{"message":"What should I do next?"}""");

        var fake = new FakePtyConnection();
        fake.OnWriteText = w => { if (w.Contains("exit\r")) fake.Exit(0); };
        fake.EnqueueRead("ready\n");

        var agent = new TestableClaudeAgent(fake)
        {
            Name = "claude",
            Options = FastOpts(new HookIntegrationOptions(_hooksPath, _fakeShim))
        };

        var result = await agent.RunAsync(new AgentRunRequest(
            Prompt: "anything",
            SessionId: Guid.NewGuid().ToString(),
            ProjectDir: _projectDir,
            Mode: InteractionMode.Interactive));

        Assert.Equal(AgentStatus.NeedsInput, result.Status);
        var q = Assert.IsType<AgentQuestion.OpenQuestion>(result.Question);
        Assert.Equal("What should I do next?", q.Text);
        Assert.Equal("claude.idle_prompt", q.Source);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public async Task NonInteractive_mode_with_question_returns_failed_with_question_kept()
    {
        WriteHook("claude.permission_prompt",
            """{"message":"Apply?","tool_name":"Edit","tool_input":{"path":"x"}}""");

        var fake = new FakePtyConnection();
        fake.OnWriteText = w => { if (w.Contains("exit\r")) fake.Exit(0); };
        fake.EnqueueRead("ready\n");

        var agent = new TestableClaudeAgent(fake)
        {
            Name = "claude",
            Options = FastOpts(new HookIntegrationOptions(_hooksPath, _fakeShim))
        };

        var result = await agent.RunAsync(new AgentRunRequest(
            Prompt: "anything",
            SessionId: Guid.NewGuid().ToString(),
            ProjectDir: _projectDir,
            Mode: InteractionMode.NonInteractive));

        Assert.Equal(AgentStatus.Failed, result.Status);
        Assert.IsType<AgentQuestion.TuiPrompt>(result.Question);
        Assert.NotNull(result.FailureReason);
        Assert.Contains("non-interactive", result.FailureReason!);
    }

    [Fact]
    public async Task Hooks_off_preserves_completed_default()
    {
        // No HookIntegrationOptions — agent should behave like before.
        var fake = new FakePtyConnection();
        fake.OnWriteText = w => { if (w.Contains("exit\r")) fake.Exit(0); };
        fake.EnqueueRead("ready\n");

        var agent = new TestableClaudeAgent(fake) { Name = "claude", Options = FastOpts() };

        var result = await agent.RunAsync(new AgentRunRequest(
            Prompt: "anything",
            SessionId: Guid.NewGuid().ToString(),
            ProjectDir: _projectDir));

        Assert.Equal(AgentStatus.Completed, result.Status);
        Assert.Null(result.Question);
    }

    [Fact]
    public async Task Hooks_on_with_empty_jsonl_still_completes()
    {
        File.WriteAllText(_hooksPath, "");

        var fake = new FakePtyConnection();
        fake.OnWriteText = w => { if (w.Contains("exit\r")) fake.Exit(0); };
        fake.EnqueueRead("ready\n");

        var agent = new TestableClaudeAgent(fake)
        {
            Name = "claude",
            Options = FastOpts(new HookIntegrationOptions(_hooksPath, _fakeShim))
        };

        var result = await agent.RunAsync(new AgentRunRequest(
            Prompt: "anything",
            SessionId: Guid.NewGuid().ToString(),
            ProjectDir: _projectDir,
            Mode: InteractionMode.Interactive));

        Assert.Equal(AgentStatus.Completed, result.Status);
    }

    [Fact]
    public async Task Install_and_uninstall_lifecycle_runs()
    {
        var settingsPath = Path.Combine(_projectDir, ClaudeHookConfig.SettingsRelative);
        bool sawSettingsDuringRun = false;

        var fake = new FakePtyConnection();
        fake.OnWriteText = w =>
        {
            // We poke between the launch line and exit — settings.json should
            // already be on disk by the time the agent writes "claude " into
            // the PTY.
            if (w.Contains("claude ") && File.Exists(settingsPath))
                sawSettingsDuringRun = true;
            if (w.Contains("exit\r")) fake.Exit(0);
        };
        fake.EnqueueRead("ready\n");

        var agent = new TestableClaudeAgent(fake)
        {
            Name = "claude",
            Options = FastOpts(new HookIntegrationOptions(_hooksPath, _fakeShim))
        };

        await agent.RunAsync(new AgentRunRequest(
            Prompt: "anything",
            SessionId: Guid.NewGuid().ToString(),
            ProjectDir: _projectDir,
            Mode: InteractionMode.Interactive));

        Assert.True(sawSettingsDuringRun, "settings.json should exist between Install and Uninstall");
        Assert.False(File.Exists(settingsPath), "Uninstall should remove settings.json after the run");
    }

    [Fact]
    public async Task NonInteractive_mode_appends_unattended_directive_to_launch_line()
    {
        var fake = new FakePtyConnection();
        fake.OnWriteText = w => { if (w.Contains("exit\r")) fake.Exit(0); };
        fake.EnqueueRead("ready\n");

        var agent = new TestableClaudeAgent(fake) { Name = "claude", Options = FastOpts() };
        await agent.RunAsync(new AgentRunRequest(
            Prompt: "anything",
            SessionId: Guid.NewGuid().ToString(),
            ProjectDir: _projectDir,
            Mode: InteractionMode.NonInteractive));

        var captured = fake.Captured.ToString();
        Assert.Contains("--append-system-prompt", captured);
        Assert.Contains(UnattendedDirective.Sentinel, captured);
    }

    [Fact]
    public async Task Interactive_mode_does_not_inject_directive()
    {
        var fake = new FakePtyConnection();
        fake.OnWriteText = w => { if (w.Contains("exit\r")) fake.Exit(0); };
        fake.EnqueueRead("ready\n");

        var agent = new TestableClaudeAgent(fake) { Name = "claude", Options = FastOpts() };
        await agent.RunAsync(new AgentRunRequest(
            Prompt: "anything",
            SessionId: Guid.NewGuid().ToString(),
            ProjectDir: _projectDir,
            Mode: InteractionMode.Interactive));

        var captured = fake.Captured.ToString();
        Assert.DoesNotContain(UnattendedDirective.Sentinel, captured);
    }

    [Fact]
    public async Task NonInteractive_violation_event_fires_when_question_detected()
    {
        WriteHook("claude.idle_prompt", """{"message":"Which one?"}""");

        AgentEvent.NonInteractiveViolation? violation = null;
        var sink = new CapturingSink(evt =>
        {
            if (evt is AgentEvent.NonInteractiveViolation v) violation = v;
        });

        var fake = new FakePtyConnection();
        fake.OnWriteText = w => { if (w.Contains("exit\r")) fake.Exit(0); };
        fake.EnqueueRead("ready\n");

        var agent = new TestableClaudeAgent(fake)
        {
            Name = "claude",
            Sink = sink,
            Options = FastOpts(new HookIntegrationOptions(_hooksPath, _fakeShim))
        };

        await agent.RunAsync(new AgentRunRequest(
            Prompt: "anything",
            SessionId: Guid.NewGuid().ToString(),
            ProjectDir: _projectDir,
            Mode: InteractionMode.NonInteractive));

        Assert.NotNull(violation);
        Assert.Equal("claude.idle_prompt", violation!.QuestionSource);
        Assert.Equal("Which one?",         violation.QuestionText);
    }

    private void WriteHook(string source, string payloadJson)
    {
        var line = $$"""{"source":"{{source}}","sessionId":"s","cwd":"c","payload":{{payloadJson}}}""";
        File.AppendAllText(_hooksPath, line + "\n");
    }

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
