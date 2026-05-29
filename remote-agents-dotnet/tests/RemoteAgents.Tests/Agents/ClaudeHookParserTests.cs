using System.Text.Json;
using RemoteAgents.Agents;

namespace RemoteAgents.Tests.Agents;

public class ClaudeHookParserTests
{
    private readonly ClaudeHookParser _parser = new();

    [Fact]
    public void Unknown_source_returns_null()
    {
        Assert.Null(_parser.TryParse(Wrap("claude.unknown", """{"message":"x"}""")));
    }

    [Fact]
    public void Non_object_root_returns_null()
    {
        Assert.Null(_parser.TryParse(JsonDocument.Parse("[]").RootElement));
    }

    [Fact]
    public void Missing_payload_returns_null()
    {
        var line = JsonDocument.Parse("""{"source":"claude.idle_prompt"}""").RootElement;
        Assert.Null(_parser.TryParse(line));
    }

    [Fact]
    public void Permission_prompt_maps_to_tui_prompt()
    {
        var line = Wrap("claude.permission_prompt",
            """{"message":"Apply this edit?","tool_name":"Edit","tool_input":{"file_path":"/tmp/x"}}""");

        var q = Assert.IsType<AgentQuestion.TuiPrompt>(_parser.TryParse(line));
        Assert.Equal("Apply this edit?", q.Text);
        Assert.Equal("Edit", q.ToolName);
        Assert.Equal("/tmp/x", q.ToolInput.GetProperty("file_path").GetString());
        Assert.Equal("claude.permission_prompt", q.Source);
    }

    [Fact]
    public void Permission_prompt_with_no_tool_input_falls_back_to_empty_object()
    {
        var line = Wrap("claude.permission_prompt",
            """{"message":"hi","tool_name":"Bash"}""");

        var q = Assert.IsType<AgentQuestion.TuiPrompt>(_parser.TryParse(line));
        Assert.Equal(JsonValueKind.Object, q.ToolInput.ValueKind);
        Assert.False(q.ToolInput.EnumerateObject().Any());
    }

    [Fact]
    public void Idle_prompt_maps_to_open_question_not_from_sentinel()
    {
        var line = Wrap("claude.idle_prompt", """{"message":"What's next?"}""");

        var q = Assert.IsType<AgentQuestion.OpenQuestion>(_parser.TryParse(line));
        Assert.Equal("What's next?", q.Text);
        Assert.False(q.FromSentinel);
        Assert.Equal("claude.idle_prompt", q.Source);
    }

    [Fact]
    public void Elicitation_dialog_maps_to_open_question()
    {
        var line = Wrap("claude.elicitation_dialog", """{"message":"pick one"}""");

        var q = Assert.IsType<AgentQuestion.OpenQuestion>(_parser.TryParse(line));
        Assert.Equal("claude.elicitation_dialog", q.Source);
    }

    [Fact]
    public void Stop_with_plain_completion_returns_null()
    {
        var line = Wrap("claude.stop", """{"last_assistant_message":"PONG"}""");
        Assert.Null(_parser.TryParse(line));
    }

    [Fact]
    public void Stop_with_sentinel_maps_to_open_question_from_sentinel()
    {
        var line = Wrap("claude.stop",
            """{"last_assistant_message":"Looked at the configs.\n<<NEEDS_INPUT>>\nWhich region default?"}""");

        var q = Assert.IsType<AgentQuestion.OpenQuestion>(_parser.TryParse(line));
        Assert.True(q.FromSentinel);
        Assert.Equal("claude.stop.sentinel", q.Source);
        Assert.Equal("Which region default?", q.Text);
    }

    [Fact]
    public void Stop_with_interrogative_heuristic_maps_to_open_question()
    {
        var line = Wrap("claude.stop",
            """{"last_assistant_message":"I see two ways.\n\nWhich would you prefer?"}""");

        var q = Assert.IsType<AgentQuestion.OpenQuestion>(_parser.TryParse(line));
        Assert.False(q.FromSentinel);
        Assert.Equal("claude.stop.heuristic", q.Source);
    }

    [Fact]
    public void Stop_failure_returns_null_even_with_question_text()
    {
        var line = Wrap("claude.stop_failure",
            """{"last_assistant_message":"Could you confirm the path?"}""");
        Assert.Null(_parser.TryParse(line));
    }

    private static JsonElement Wrap(string source, string payloadJson)
    {
        var line = $$"""{"source":"{{source}}","sessionId":"s","cwd":"c","payload":{{payloadJson}}}""";
        return JsonDocument.Parse(line).RootElement;
    }
}
