using RemoteAgents.Actors.Agents;
using RemoteAgents.Actors.Agents.Codex;

namespace RemoteAgents.Tests;

public class CodexProtocolTests
{
    [Fact]
    public void BuildArgs_fresh_run_starts_with_exec_and_reads_stdin()
    {
        var args = CodexProtocol.BuildArgs(null, "C:/proj", "C:/tmp/last.txt", "gpt-5.5", "read-only");

        Assert.Equal("exec", args[0]);
        Assert.DoesNotContain("resume", args);
        Assert.Equal("-", args[^1]);
    }

    [Fact]
    public void BuildArgs_resumed_run_threads_the_session_id()
    {
        var args = CodexProtocol.BuildArgs("sess-12345678", "C:/proj", "C:/tmp/last.txt", "gpt-5.5", "read-only");

        Assert.Equal(new[] { "exec", "resume", "sess-12345678" }, args.Take(3));
    }

    [Fact]
    public void BuildArgs_carries_dir_output_sandbox_model_and_json()
    {
        var args = CodexProtocol.BuildArgs(null, "C:/proj", "C:/tmp/last.txt", "gpt-5.5", "read-only");

        AssertPair(args, "--cd", "C:/proj");
        AssertPair(args, "-o", "C:/tmp/last.txt");
        AssertPair(args, "--sandbox", "read-only");
        AssertPair(args, "--model", "gpt-5.5");
        Assert.Contains("--json", args);
        Assert.Contains("--skip-git-repo-check", args);
    }

    [Fact]
    public void BuildArgs_omits_model_flag_when_model_is_blank()
    {
        var args = CodexProtocol.BuildArgs(null, "C:/proj", "C:/tmp/last.txt", "", "read-only");

        Assert.DoesNotContain("--model", args);
    }

    [Theory]
    [InlineData("{\"thread_id\":\"abcd1234ef\"}")]
    [InlineData("{\"session_id\":\"abcd1234ef\"}")]
    [InlineData("{\"sessionId\":\"abcd1234ef\"}")]
    [InlineData("{\"thread\":{\"id\":\"abcd1234ef\"}}")]
    [InlineData("{\"session\":{\"id\":\"abcd1234ef\"}}")]
    [InlineData("{\"payload\":{\"thread_id\":\"abcd1234ef\"}}")]
    public void ScanSessionId_finds_the_id_across_known_shapes(string line)
    {
        Assert.Equal("abcd1234ef", CodexProtocol.ScanSessionId(line));
    }

    [Theory]
    [InlineData("codex_core::exec ERROR not json")]
    [InlineData("{\"thread_id\":\"short\"}")]
    [InlineData("{\"other\":\"abcd1234ef\"}")]
    public void ScanSessionId_returns_null_when_no_id_present(string line)
    {
        Assert.Null(CodexProtocol.ScanSessionId(line));
    }

    [Fact]
    public void ExtractTranscript_agent_message_becomes_a_text_turn()
    {
        var turns = CodexProtocol.ExtractTranscript(
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"hello\"}}");

        var turn = Assert.Single(turns);
        Assert.Equal(AgentTurnKind.Text, turn.Kind);
        Assert.Equal("hello", turn.Body);
    }

    [Fact]
    public void ExtractTranscript_command_execution_becomes_tooluse_then_toolresult()
    {
        var turns = CodexProtocol.ExtractTranscript(
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"command_execution\",\"command\":\"ls\",\"aggregated_output\":\"a.txt\",\"exit_code\":0}}");

        Assert.Equal(2, turns.Length);
        Assert.Equal(AgentTurnKind.ToolUse, turns[0].Kind);
        Assert.Contains("\"command\":\"ls\"", turns[0].Body);
        Assert.Equal(AgentTurnKind.ToolResult, turns[1].Kind);
        Assert.Equal("[exit 0]\na.txt", turns[1].Body);
    }

    [Fact]
    public void ExtractTranscript_reasoning_becomes_a_thinking_turn()
    {
        var turns = CodexProtocol.ExtractTranscript(
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"reasoning\",\"text\":\"hmm\"}}");

        var turn = Assert.Single(turns);
        Assert.Equal(AgentTurnKind.Thinking, turn.Kind);
        Assert.Equal("hmm", turn.Body);
    }

    [Fact]
    public void ExtractTranscript_skips_non_json_tracing_and_incomplete_events()
    {
        var raw = string.Join('\n',
            "codex_core::exec ERROR something",
            "{\"type\":\"turn.started\"}",
            "{\"type\":\"item.started\",\"item\":{\"type\":\"agent_message\",\"text\":\"draft\"}}",
            "{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"final\"}}");

        var turn = Assert.Single(CodexProtocol.ExtractTranscript(raw));
        Assert.Equal("final", turn.Body);
    }

    [Fact]
    public void ExtractTranscript_empty_output_yields_no_turns()
    {
        Assert.Empty(CodexProtocol.ExtractTranscript(""));
    }

    private static void AssertPair(List<string> args, string flag, string value)
    {
        var i = args.IndexOf(flag);
        Assert.True(i >= 0 && i + 1 < args.Count, $"missing flag {flag}");
        Assert.Equal(value, args[i + 1]);
    }
}
