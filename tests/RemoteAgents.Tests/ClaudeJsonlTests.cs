using RemoteAgents.Actors.Agents;
using RemoteAgents.Actors.Agents.Claude;

namespace RemoteAgents.Tests;

public class ClaudeJsonlTests : IDisposable
{
    private readonly string _dir;
    private readonly string _sessionId;
    private readonly string _jsonlPath;

    public ClaudeJsonlTests()
    {
        _sessionId = Guid.NewGuid().ToString();
        // Stage under a randomly-named projects subdir; resolution is by
        // sessionId, so the folder name is irrelevant — which is the point.
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "projects", "ra-jsonl-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _jsonlPath = Path.Combine(_dir, _sessionId + ".jsonl");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void ResolveSessionFile_finds_the_file_by_id_regardless_of_folder()
    {
        File.WriteAllText(_jsonlPath, "{}");
        Assert.Equal(_jsonlPath, ClaudeJsonl.ResolveSessionFile(_sessionId));
    }

    [Fact]
    public void ResolveSessionFile_returns_null_for_unknown_id()
    {
        Assert.Null(ClaudeJsonl.ResolveSessionFile(Guid.NewGuid().ToString()));
    }

    [Fact]
    public void TryReadLastAssistantText_missing_file_returns_null()
    {
        Assert.Null(ClaudeJsonl.TryReadLastAssistantText(_sessionId));
    }

    [Fact]
    public void TryReadLastAssistantText_returns_assistant_text_after_user_prompt()
    {
        File.WriteAllLines(_jsonlPath, new[]
        {
            UserLine("Hello, please summarize."),
            AssistantTextLine("Sure — here is a summary."),
        });

        var result = ClaudeJsonl.TryReadLastAssistantText(_sessionId, "Hello, please summarize.");
        Assert.Equal("Sure — here is a summary.", result);
    }

    [Fact]
    public void TryReadLastAssistantText_concatenates_multiple_text_blocks()
    {
        File.WriteAllLines(_jsonlPath, new[]
        {
            UserLine("Two parts please."),
            AssistantTextLine("Part one."),
            AssistantTextLine("Part two."),
        });

        var result = ClaudeJsonl.TryReadLastAssistantText(_sessionId, "Two parts please.");
        Assert.Equal("Part one.\nPart two.", result);
    }

    [Fact]
    public void TryReadLastAssistantText_skips_tool_use_and_thinking_blocks()
    {
        File.WriteAllLines(_jsonlPath, new[]
        {
            UserLine("Run the thing."),
            AssistantMixedLine(thinking: "internal reasoning", text: "OK, running.", toolName: "Bash"),
            AssistantTextLine("Done."),
        });

        var result = ClaudeJsonl.TryReadLastAssistantText(_sessionId, "Run the thing.");
        Assert.Equal("OK, running.\nDone.", result);
        Assert.DoesNotContain("internal reasoning", result);
        Assert.DoesNotContain("Bash", result);
    }

    [Fact]
    public void TryReadLastAssistantText_with_prompt_hint_anchors_on_matching_user_message()
    {
        File.WriteAllLines(_jsonlPath, new[]
        {
            UserLine("Previous turn."),
            AssistantTextLine("Previous answer."),
            UserLine("Fix the bug at line 42."),
            AssistantTextLine("Fixed."),
        });

        var result = ClaudeJsonl.TryReadLastAssistantText(_sessionId, "Fix the bug at line 42.");
        Assert.Equal("Fixed.", result);
    }

    [Fact]
    public void TryReadLastAssistantText_falls_back_to_last_user_when_hint_missing()
    {
        File.WriteAllLines(_jsonlPath, new[]
        {
            UserLine("First prompt."),
            AssistantTextLine("First answer."),
            UserLine("Second prompt."),
            AssistantTextLine("Second answer."),
        });

        var result = ClaudeJsonl.TryReadLastAssistantText(_sessionId, promptHint: null);
        Assert.Equal("Second answer.", result);
    }

    [Fact]
    public void TryReadLastAssistantText_ignores_internal_tool_result_user_messages()
    {
        File.WriteAllLines(_jsonlPath, new[]
        {
            UserLine("Run tests please."),
            AssistantTextLine("Running them now."),
            UserToolResultLine("tests passed: 42"),
            AssistantTextLine("All 42 tests passed."),
        });

        var result = ClaudeJsonl.TryReadLastAssistantText(_sessionId, "Run tests please.");
        Assert.Equal("Running them now.\nAll 42 tests passed.", result);
    }

    [Fact]
    public void TryReadLastAssistantText_treats_string_content_as_a_text_block()
    {
        File.WriteAllLines(_jsonlPath, new[]
        {
            UserLine("Hello."),
            "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":\"bare string\"}}",
        });

        var result = ClaudeJsonl.TryReadLastAssistantText(_sessionId, "Hello.");
        Assert.Equal("bare string", result);
    }

    [Fact]
    public void TryReadLastAssistantText_no_assistant_text_returns_empty_string()
    {
        File.WriteAllLines(_jsonlPath, new[]
        {
            UserLine("Just compile."),
            AssistantMixedLine(thinking: null, text: null, toolName: "Bash"),
        });

        var result = ClaudeJsonl.TryReadLastAssistantText(_sessionId, "Just compile.");
        Assert.Equal("", result);
    }

    [Fact]
    public void TryReadLastTurnTranscript_missing_file_returns_null()
    {
        Assert.Null(ClaudeJsonl.TryReadLastTurnTranscript(_sessionId));
    }

    [Fact]
    public void TryReadLastTurnTranscript_returns_all_block_kinds_in_order()
    {
        File.WriteAllLines(_jsonlPath, new[]
        {
            UserLine("Compile please."),
            AssistantMixedLine(thinking: "Need to run dotnet build.", text: "Compiling.", toolName: "Bash"),
            UserToolResultLine("Build succeeded."),
            AssistantTextLine("All green."),
        });

        var turns = ClaudeJsonl.TryReadLastTurnTranscript(_sessionId, "Compile please.");

        Assert.NotNull(turns);
        Assert.Equal(5, turns!.Length);
        Assert.Equal(AgentTurnKind.Thinking, turns[0].Kind);
        Assert.Equal("Need to run dotnet build.", turns[0].Body);
        Assert.Equal(AgentTurnKind.Text, turns[1].Kind);
        Assert.Equal("Compiling.", turns[1].Body);
        Assert.Equal(AgentTurnKind.ToolUse, turns[2].Kind);
        Assert.Contains("\"name\":\"Bash\"", turns[2].Body);
        Assert.Equal(AgentTurnKind.ToolResult, turns[3].Kind);
        Assert.Equal("Build succeeded.", turns[3].Body);
        Assert.Equal(AgentTurnKind.Text, turns[4].Kind);
        Assert.Equal("All green.", turns[4].Body);
    }

    [Fact]
    public void TryReadLastTurnTranscript_keeps_full_tool_input_args()
    {
        var bigInput = "{\"file_path\":\"foo.cs\",\"content\":\"" + new string('x', 4000) + "\"}";
        var line = "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":["
                 + "{\"type\":\"tool_use\",\"name\":\"Write\",\"input\":" + bigInput + "}"
                 + "]}}";
        File.WriteAllLines(_jsonlPath, new[] { UserLine("Write the file."), line });

        var turns = ClaudeJsonl.TryReadLastTurnTranscript(_sessionId, "Write the file.");

        Assert.Single(turns!);
        Assert.Equal(AgentTurnKind.ToolUse, turns![0].Kind);
        Assert.Contains(new string('x', 4000), turns[0].Body);
    }

    private static string UserLine(string text)
        => $"{{\"type\":\"user\",\"message\":{{\"role\":\"user\",\"content\":[{{\"type\":\"text\",\"text\":{Quote(text)}}}]}}}}";

    private static string UserToolResultLine(string output)
        => $"{{\"type\":\"user\",\"message\":{{\"role\":\"user\",\"content\":[{{\"type\":\"tool_result\",\"content\":{Quote(output)}}}]}}}}";

    private static string AssistantTextLine(string text)
        => $"{{\"type\":\"assistant\",\"message\":{{\"role\":\"assistant\",\"content\":[{{\"type\":\"text\",\"text\":{Quote(text)}}}]}}}}";

    private static string AssistantMixedLine(string? thinking, string? text, string? toolName)
    {
        var blocks = new List<string>();
        if (thinking is not null) blocks.Add($"{{\"type\":\"thinking\",\"thinking\":{Quote(thinking)}}}");
        if (text is not null) blocks.Add($"{{\"type\":\"text\",\"text\":{Quote(text)}}}");
        if (toolName is not null) blocks.Add($"{{\"type\":\"tool_use\",\"name\":{Quote(toolName)},\"input\":{{}}}}");
        return $"{{\"type\":\"assistant\",\"message\":{{\"role\":\"assistant\",\"content\":[{string.Join(',', blocks)}]}}}}";
    }

    private static string Quote(string s)
        => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
