using RemoteAgents.Agents;
using RemoteAgents.Providers.Codex;

namespace RemoteAgents.Tests.Agents;

// Parses the codex --json event stream into AgentTurn[]. Fixture text
// is a real codex-cli 0.134.0 stdout sample captured from a tiny exec
// run on Windows (mix of JSON event lines and non-JSON tracing).
public class CodexJsonlTests
{
    [Fact]
    public void ExtractTranscript_empty_or_whitespace_returns_empty()
    {
        Assert.Empty(CodexJsonl.ExtractTranscript(""));
        Assert.Empty(CodexJsonl.ExtractTranscript("   \r\n  "));
        Assert.Empty(CodexJsonl.ExtractTranscript(null!));
    }

    [Fact]
    public void ExtractTranscript_skips_non_json_tracing_lines()
    {
        var stdout = string.Join('\n', new[]
        {
            "2026-05-30T21:55:59.281240Z ERROR codex_core::exec: exec error",
            "not json at all",
            "{\"type\":\"thread.started\",\"thread_id\":\"019e\"}",
        });

        // No item.completed events → empty transcript even though some
        // JSON lines parsed successfully.
        Assert.Empty(CodexJsonl.ExtractTranscript(stdout));
    }

    [Fact]
    public void ExtractTranscript_translates_agent_message_to_text()
    {
        var line = "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_0\",\"type\":\"agent_message\",\"text\":\"Hello world.\"}}";
        var turns = CodexJsonl.ExtractTranscript(line);

        Assert.Single(turns);
        Assert.Equal(AgentTurnKind.Text, turns[0].Kind);
        Assert.Equal("Hello world.",     turns[0].Body);
    }

    [Fact]
    public void ExtractTranscript_translates_command_execution_to_tool_use_and_result()
    {
        var line = "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_1\","
                 + "\"type\":\"command_execution\","
                 + "\"command\":\"pwsh.exe -Command 'Get-Content foo.txt'\","
                 + "\"aggregated_output\":\"file body here\","
                 + "\"exit_code\":0,\"status\":\"completed\"}}";

        var turns = CodexJsonl.ExtractTranscript(line);

        Assert.Equal(2, turns.Length);
        Assert.Equal(AgentTurnKind.ToolUse,    turns[0].Kind);
        Assert.Contains("\"name\":\"shell\"", turns[0].Body);
        Assert.Contains("Get-Content foo.txt", turns[0].Body);
        Assert.Equal(AgentTurnKind.ToolResult, turns[1].Kind);
        Assert.Contains("[exit 0]",            turns[1].Body);
        Assert.Contains("file body here",      turns[1].Body);
    }

    [Fact]
    public void ExtractTranscript_failed_command_keeps_exit_and_output()
    {
        var line = "{\"type\":\"item.completed\",\"item\":{\"type\":\"command_execution\","
                 + "\"command\":\"ls /nope\",\"aggregated_output\":\"execution error: foo\","
                 + "\"exit_code\":-1,\"status\":\"failed\"}}";

        var turns = CodexJsonl.ExtractTranscript(line);

        Assert.Equal(2, turns.Length);
        Assert.Contains("[exit -1]",          turns[1].Body);
        Assert.Contains("execution error",    turns[1].Body);
    }

    [Fact]
    public void ExtractTranscript_translates_reasoning_to_thinking_with_text_or_summary()
    {
        var stdout = string.Join('\n', new[]
        {
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"reasoning\",\"text\":\"step-by-step trace\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"reasoning\",\"summary\":\"summarized thinking\"}}",
        });

        var turns = CodexJsonl.ExtractTranscript(stdout);

        Assert.Equal(2, turns.Length);
        Assert.Equal(AgentTurnKind.Thinking, turns[0].Kind);
        Assert.Equal("step-by-step trace",   turns[0].Body);
        Assert.Equal(AgentTurnKind.Thinking, turns[1].Kind);
        Assert.Equal("summarized thinking",  turns[1].Body);
    }

    [Fact]
    public void ExtractTranscript_skips_unknown_item_types_and_non_completed_events()
    {
        var stdout = string.Join('\n', new[]
        {
            "{\"type\":\"thread.started\",\"thread_id\":\"019e\"}",
            "{\"type\":\"turn.started\"}",
            "{\"type\":\"item.started\",\"item\":{\"type\":\"command_execution\",\"command\":\"x\",\"status\":\"in_progress\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"only-real-turn\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"future_unknown_kind\",\"data\":{}}}",
            "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":42}}",
        });

        var turns = CodexJsonl.ExtractTranscript(stdout);

        Assert.Single(turns);
        Assert.Equal("only-real-turn", turns[0].Body);
    }

    [Fact]
    public void ExtractTranscript_handles_real_world_mixed_stream()
    {
        // Captured from a real codex-cli 0.134.0 run on Windows: 5 events
        // interleaved with two non-JSON ERROR lines from codex_core.
        var stdout = string.Join('\n', new[]
        {
            "{\"type\":\"thread.started\",\"thread_id\":\"019e7ae2-f436\"}",
            "{\"type\":\"turn.started\"}",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_0\",\"type\":\"agent_message\",\"text\":\"I will read test.txt.\"}}",
            "{\"type\":\"item.started\",\"item\":{\"id\":\"item_1\",\"type\":\"command_execution\",\"command\":\"pwsh.exe -Command 'Get-Content -LiteralPath test.txt'\",\"status\":\"in_progress\"}}",
            "2026-05-30T21:55:59.281240Z ERROR codex_core::exec: exec error: windows sandbox: spawn setup refresh",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_1\",\"type\":\"command_execution\",\"command\":\"pwsh.exe -Command 'Get-Content -LiteralPath test.txt'\",\"aggregated_output\":\"execution error\",\"exit_code\":-1,\"status\":\"failed\"}}",
            "2026-05-30T21:55:59.283273Z ERROR codex_core::tools::router: error=execution error",
            "{\"type\":\"item.completed\",\"item\":{\"id\":\"item_2\",\"type\":\"agent_message\",\"text\":\"hello\\n\\nI couldn't read test.txt.\\n\\ndone\"}}",
            "{\"type\":\"turn.completed\",\"usage\":{\"input_tokens\":32893}}",
        });

        var turns = CodexJsonl.ExtractTranscript(stdout);

        // 1 agent_message + 1 command_execution (2 turns) + 1 agent_message = 4 turns
        Assert.Equal(4, turns.Length);
        Assert.Equal(AgentTurnKind.Text,       turns[0].Kind);
        Assert.Equal(AgentTurnKind.ToolUse,    turns[1].Kind);
        Assert.Equal(AgentTurnKind.ToolResult, turns[2].Kind);
        Assert.Equal(AgentTurnKind.Text,       turns[3].Kind);
        Assert.Contains("hello",               turns[3].Body);
        Assert.Contains("done",                turns[3].Body);
    }
}
