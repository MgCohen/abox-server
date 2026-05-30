using System.Text.Json;
using RemoteAgents.Agents;
using RemoteAgents.Agents.Hooks;

namespace RemoteAgents.Tests.Agents;

public class CodexHookParserTests
{
    private readonly CodexHookParser _parser = new();

    [Fact]
    public void Unknown_source_returns_null()
    {
        Assert.Null(_parser.TryParse(Wrap("codex.unknown", """{"message":"x"}""")));
    }

    [Fact]
    public void Permission_request_maps_to_tui_prompt()
    {
        var line = Wrap("codex.permission_request",
            """{"message":"Run this?","tool_name":"Bash","tool_input":{"command":"ls"}}""");

        var q = Assert.IsType<AgentQuestion.TuiPrompt>(_parser.TryParse(line));
        Assert.Equal("Run this?", q.Text);
        Assert.Equal("Bash", q.ToolName);
        Assert.Equal("ls", q.ToolInput.GetProperty("command").GetString());
        Assert.Equal("codex.permission_request", q.Source);
    }

    [Fact]
    public void Stop_with_sentinel_marks_from_sentinel_and_strips_prefix()
    {
        var line = Wrap("codex.stop",
            """{"last_assistant_message":"Looked at the configs.\n<<NEEDS_INPUT>>\nShould the default be us-east or eu-west?"}""");

        var q = Assert.IsType<AgentQuestion.OpenQuestion>(_parser.TryParse(line));
        Assert.True(q.FromSentinel);
        Assert.Equal("codex.stop.sentinel", q.Source);
        Assert.Equal("Should the default be us-east or eu-west?", q.Text);
    }

    [Fact]
    public void Stop_with_sentinel_at_end_keeps_full_text()
    {
        var line = Wrap("codex.stop",
            """{"last_assistant_message":"Stuck.\n<<NEEDS_INPUT>>"}""");

        var q = Assert.IsType<AgentQuestion.OpenQuestion>(_parser.TryParse(line));
        Assert.True(q.FromSentinel);
        Assert.Contains("Stuck.", q.Text);
    }

    [Fact]
    public void Stop_with_interrogative_lead_in_last_paragraph_matches_heuristic()
    {
        var line = Wrap("codex.stop",
            """{"last_assistant_message":"I see two approaches.\n\nWhich would you prefer?"}""");

        var q = Assert.IsType<AgentQuestion.OpenQuestion>(_parser.TryParse(line));
        Assert.False(q.FromSentinel);
        Assert.Equal("codex.stop.heuristic", q.Source);
    }

    [Fact]
    public void Stop_with_plain_completion_returns_null()
    {
        var line = Wrap("codex.stop",
            """{"last_assistant_message":"Done. All tests passed."}""");

        Assert.Null(_parser.TryParse(line));
    }

    [Fact]
    public void Stop_with_empty_last_message_returns_null()
    {
        var line = Wrap("codex.stop", """{"last_assistant_message":""}""");
        Assert.Null(_parser.TryParse(line));
    }

    [Fact]
    public void Stop_failure_returns_null_even_with_question_text()
    {
        var line = Wrap("codex.stop_failure",
            """{"last_assistant_message":"Could you confirm the path?"}""");
        Assert.Null(_parser.TryParse(line));
    }

    [Theory]
    [InlineData("Done. All tests passed.",                                   false)] // not a question
    [InlineData("Hmm.",                                                      false)] // no "?"
    [InlineData("All checks pass?",                                          false)] // "?" but no interrogative lead
    [InlineData("Could you confirm the path?",                               true )]
    [InlineData("Should I proceed with the rename?",                         true )]
    [InlineData("Which file should I edit?",                                 true )]
    [InlineData("Do you want me to keep going?",                             true )]
    [InlineData("How would you like me to handle that?",                     true )]
    [InlineData("Would you prefer A or B?",                                  true )]
    public void LooksLikeQuestion_matches_interrogative_endings(string text, bool expected)
    {
        Assert.Equal(expected, StopPayloadInspector.LooksLikeQuestion(text));
    }

    [Fact]
    public void LooksLikeQuestion_only_considers_last_paragraph()
    {
        // Should I leave is in the first paragraph — last paragraph is a plain "?"
        var text = "Should I leave the imports as-is?\n\nThe test passes either way.\n\nLooks good?";
        Assert.False(StopPayloadInspector.LooksLikeQuestion(text));
    }

    private static JsonElement Wrap(string source, string payloadJson)
    {
        var line = $$"""{"source":"{{source}}","sessionId":"s","cwd":"c","payload":{{payloadJson}}}""";
        return JsonDocument.Parse(line).RootElement;
    }
}
