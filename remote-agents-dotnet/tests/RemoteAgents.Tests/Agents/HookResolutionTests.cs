using System.Text.Json;
using RemoteAgents.Providers.Codex;
using RemoteAgents.Providers.Claude;
using RemoteAgents.Agents;
using RemoteAgents.Agents.Hooks;

namespace RemoteAgents.Tests.Agents;

public class HookResolutionTests : IDisposable
{
    private readonly string _root;
    private readonly string _hooksPath;

    public HookResolutionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ra-hookres-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _hooksPath = Path.Combine(_root, "hooks.jsonl");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Null_path_returns_completed()
    {
        var outcome = HookResolution.FromHooksJsonl(null, new ClaudeHookParser(), InteractionMode.Interactive);
        Assert.Equal(AgentStatus.Completed, outcome.Status);
        Assert.Null(outcome.Question);
    }

    [Fact]
    public void Missing_file_returns_completed()
    {
        var outcome = HookResolution.FromHooksJsonl(_hooksPath, new ClaudeHookParser(), InteractionMode.Interactive);
        Assert.Equal(AgentStatus.Completed, outcome.Status);
    }

    [Fact]
    public void Empty_file_returns_completed()
    {
        File.WriteAllText(_hooksPath, "");
        var outcome = HookResolution.FromHooksJsonl(_hooksPath, new ClaudeHookParser(), InteractionMode.Interactive);
        Assert.Equal(AgentStatus.Completed, outcome.Status);
    }

    [Fact]
    public void Malformed_lines_are_skipped()
    {
        File.WriteAllText(_hooksPath, "this is not json\n\n{broken\n");
        var outcome = HookResolution.FromHooksJsonl(_hooksPath, new ClaudeHookParser(), InteractionMode.Interactive);
        Assert.Equal(AgentStatus.Completed, outcome.Status);
    }

    [Fact]
    public void Interactive_mode_with_question_returns_needs_input()
    {
        WriteHook("claude.idle_prompt", """{"message":"What now?"}""");

        var outcome = HookResolution.FromHooksJsonl(_hooksPath, new ClaudeHookParser(), InteractionMode.Interactive);

        Assert.Equal(AgentStatus.NeedsInput, outcome.Status);
        var q = Assert.IsType<AgentQuestion.OpenQuestion>(outcome.Question);
        Assert.Equal("What now?", q.Text);
        Assert.Null(outcome.FailureReason);
    }

    [Fact]
    public void NonInteractive_mode_with_question_returns_failed_with_reason()
    {
        WriteHook("claude.idle_prompt", """{"message":"What now?"}""");

        var outcome = HookResolution.FromHooksJsonl(_hooksPath, new ClaudeHookParser(), InteractionMode.NonInteractive);

        Assert.Equal(AgentStatus.Failed, outcome.Status);
        Assert.NotNull(outcome.Question);
        Assert.NotNull(outcome.FailureReason);
        Assert.Contains("non-interactive",  outcome.FailureReason!);
        Assert.Contains("claude.idle_prompt", outcome.FailureReason!);
    }

    [Fact]
    public void First_detected_question_wins_when_multiple_present()
    {
        WriteHook("claude.permission_prompt", """{"message":"first","tool_name":"Edit","tool_input":{}}""");
        WriteHook("claude.idle_prompt",       """{"message":"second"}""");

        var outcome = HookResolution.FromHooksJsonl(_hooksPath, new ClaudeHookParser(), InteractionMode.Interactive);

        var q = Assert.IsType<AgentQuestion.TuiPrompt>(outcome.Question);
        Assert.Equal("first", q.Text);
    }

    [Fact]
    public void Codex_parser_routes_through_same_helper()
    {
        WriteHook("codex.stop", """{"last_assistant_message":"Stuck.\n<<NEEDS_INPUT>>\nWhich path?"}""");

        var outcome = HookResolution.FromHooksJsonl(_hooksPath, new CodexHookParser(), InteractionMode.Interactive);

        Assert.Equal(AgentStatus.NeedsInput, outcome.Status);
        var q = Assert.IsType<AgentQuestion.OpenQuestion>(outcome.Question);
        Assert.True(q.FromSentinel);
    }

    private void WriteHook(string source, string payloadJson)
    {
        var line = $$"""{"source":"{{source}}","sessionId":"s","cwd":"c","payload":{{payloadJson}}}""";
        File.AppendAllText(_hooksPath, line + "\n");
    }
}
