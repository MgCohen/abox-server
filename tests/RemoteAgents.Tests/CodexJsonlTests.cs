using RemoteAgents.Actors.Agents;
using RemoteAgents.Actors.Agents.Codex;

namespace RemoteAgents.Tests;

public class CodexJsonlTests
{
    [Fact]
    public void Agent_message_becomes_a_text_turn()
    {
        var turns = CodexJsonl.ExtractTranscript(
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"hello\"}}");

        var turn = Assert.Single(turns);
        Assert.Equal(AgentTurnKind.Text, turn.Kind);
        Assert.Equal("hello", turn.Body);
    }

    [Fact]
    public void Command_execution_becomes_tooluse_then_toolresult()
    {
        var turns = CodexJsonl.ExtractTranscript(
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"command_execution\",\"command\":\"ls\",\"aggregated_output\":\"a.txt\",\"exit_code\":0}}");

        Assert.Equal(2, turns.Length);
        Assert.Equal(AgentTurnKind.ToolUse, turns[0].Kind);
        Assert.Contains("\"command\":\"ls\"", turns[0].Body);
        Assert.Equal(AgentTurnKind.ToolResult, turns[1].Kind);
        Assert.Equal("[exit 0]\na.txt", turns[1].Body);
    }

    [Fact]
    public void Reasoning_becomes_a_thinking_turn()
    {
        var turns = CodexJsonl.ExtractTranscript(
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"reasoning\",\"text\":\"hmm\"}}");

        var turn = Assert.Single(turns);
        Assert.Equal(AgentTurnKind.Thinking, turn.Kind);
        Assert.Equal("hmm", turn.Body);
    }

    [Fact]
    public void Skips_non_json_tracing_and_incomplete_events()
    {
        var raw = string.Join('\n',
            "codex_core::exec ERROR something",
            "{\"type\":\"turn.started\"}",
            "{\"type\":\"item.started\",\"item\":{\"type\":\"agent_message\",\"text\":\"draft\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"final\"}}");

        var turn = Assert.Single(CodexJsonl.ExtractTranscript(raw));
        Assert.Equal("final", turn.Body);
    }

    [Fact]
    public void Empty_output_yields_no_turns()
    {
        Assert.Empty(CodexJsonl.ExtractTranscript(""));
    }
}
