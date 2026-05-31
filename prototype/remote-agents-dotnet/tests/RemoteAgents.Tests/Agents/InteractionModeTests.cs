using System.Text.Json;
using RemoteAgents.Agents;

namespace RemoteAgents.Tests.Agents;

public class InteractionModeTests
{
    [Fact]
    public void AgentRunRequest_defaults_to_non_interactive()
    {
        var req = new AgentRunRequest("do thing", null, "C:/proj");
        Assert.Equal(InteractionMode.NonInteractive, req.Mode);
    }

    [Fact]
    public void AgentRunRequest_accepts_interactive_mode()
    {
        var req = new AgentRunRequest("do thing", null, "C:/proj", InteractionMode.Interactive);
        Assert.Equal(InteractionMode.Interactive, req.Mode);
    }

    [Fact]
    public void AgentResult_defaults_to_completed_with_no_question()
    {
        var r = new AgentResult("text", "abc", 0, "raw");
        Assert.Equal(AgentStatus.Completed, r.Status);
        Assert.Null(r.Question);
        Assert.Null(r.FailureReason);
    }

    [Fact]
    public void AgentResult_carries_question_when_needs_input()
    {
        var payload = ParseJson("""{"hook":"idle_prompt"}""");
        var q = new AgentQuestion.OpenQuestion(
            Text:         "Which approach do you want?",
            FromSentinel: false,
            HookPayload:  payload,
            Source:       "claude.idle_prompt");
        var r = new AgentResult("text", "abc", 0, "raw", AgentStatus.NeedsInput, q);

        Assert.Equal(AgentStatus.NeedsInput, r.Status);
        Assert.Same(q, r.Question);
    }

    [Fact]
    public void TuiPrompt_exposes_tool_name_and_input()
    {
        var payload   = ParseJson("""{"matcher":"permission_prompt"}""");
        var toolInput = ParseJson("""{"file_path":"/tmp/x"}""");
        var q = new AgentQuestion.TuiPrompt(
            Text:        "Apply this edit?",
            ToolName:    "Edit",
            ToolInput:   toolInput,
            HookPayload: payload,
            Source:      "claude.permission_prompt");

        Assert.Equal("Edit", q.ToolName);
        Assert.Equal("/tmp/x", q.ToolInput.GetProperty("file_path").GetString());
        Assert.IsAssignableFrom<AgentQuestion>(q);
    }

    [Fact]
    public void AgentQuestion_pattern_matches_each_case()
    {
        AgentQuestion tui  = new AgentQuestion.TuiPrompt("t", "Bash", ParseJson("{}"), ParseJson("{}"), "src");
        AgentQuestion open = new AgentQuestion.OpenQuestion("o", false, ParseJson("{}"), "src");

        Assert.True(tui  is AgentQuestion.TuiPrompt);
        Assert.True(open is AgentQuestion.OpenQuestion);
        Assert.False(tui  is AgentQuestion.OpenQuestion);
        Assert.False(open is AgentQuestion.TuiPrompt);
    }

    [Fact]
    public void AgentQuestion_round_trips_through_json_with_discriminator()
    {
        AgentQuestion q = new AgentQuestion.TuiPrompt(
            Text:        "Apply this edit?",
            ToolName:    "Edit",
            ToolInput:   ParseJson("""{"file_path":"/tmp/x"}"""),
            HookPayload: ParseJson("""{"hook":"permission_prompt"}"""),
            Source:      "claude.permission_prompt");

        var json = JsonSerializer.Serialize(q);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("TuiPrompt", doc.RootElement.GetProperty("kind").GetString());

        var back = JsonSerializer.Deserialize<AgentQuestion>(json);
        var tui  = Assert.IsType<AgentQuestion.TuiPrompt>(back);
        Assert.Equal("Edit", tui.ToolName);
        Assert.Equal("Apply this edit?", tui.Text);
        Assert.Equal("claude.permission_prompt", tui.Source);
    }

    [Fact]
    public void OpenQuestion_round_trips_through_json_with_discriminator()
    {
        AgentQuestion q = new AgentQuestion.OpenQuestion(
            Text:         "<<NEEDS_INPUT>> Which mode?",
            FromSentinel: true,
            HookPayload:  ParseJson("""{"hook":"stop"}"""),
            Source:       "codex.stop.sentinel");

        var json = JsonSerializer.Serialize(q);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("OpenQuestion", doc.RootElement.GetProperty("kind").GetString());

        var back = JsonSerializer.Deserialize<AgentQuestion>(json);
        var open = Assert.IsType<AgentQuestion.OpenQuestion>(back);
        Assert.True(open.FromSentinel);
        Assert.Equal("codex.stop.sentinel", open.Source);
    }

    private static JsonElement ParseJson(string s) =>
        JsonDocument.Parse(s).RootElement.Clone();
}
