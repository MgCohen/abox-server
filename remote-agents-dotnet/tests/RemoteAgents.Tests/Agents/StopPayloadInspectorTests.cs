using RemoteAgents.Agents;

namespace RemoteAgents.Tests.Agents;

// Tests for the text-only path on StopPayloadInspector — the JSON-payload
// path is covered indirectly by Claude/CodexHookParserTests. The
// text-only path exists for callers that already have the assistant's
// final message but no structured hook payload (CodexAgent text fallback).
public class StopPayloadInspectorTests
{
    [Fact]
    public void Text_with_sentinel_yields_open_question_from_sentinel()
    {
        var q = StopPayloadInspector.InspectText(
            "Looked.\n<<NEEDS_INPUT>>\nWhich path?",
            sentinelSource:  "codex.text.sentinel",
            heuristicSource: "codex.text.heuristic");

        Assert.NotNull(q);
        Assert.True(q!.FromSentinel);
        Assert.Equal("codex.text.sentinel", q.Source);
        Assert.Equal("Which path?", q.Text);
    }

    [Fact]
    public void Text_with_interrogative_lead_yields_heuristic()
    {
        var q = StopPayloadInspector.InspectText(
            "I see two paths.\n\nWhich would you prefer?",
            "codex.text.sentinel",
            "codex.text.heuristic");

        Assert.NotNull(q);
        Assert.False(q!.FromSentinel);
        Assert.Equal("codex.text.heuristic", q.Source);
    }

    [Fact]
    public void Text_plain_completion_returns_null()
    {
        Assert.Null(StopPayloadInspector.InspectText("PONG", "s", "h"));
        Assert.Null(StopPayloadInspector.InspectText("Done.", "s", "h"));
    }

    [Fact]
    public void Empty_or_whitespace_text_returns_null()
    {
        Assert.Null(StopPayloadInspector.InspectText("",     "s", "h"));
        Assert.Null(StopPayloadInspector.InspectText("   \n", "s", "h"));
    }
}
