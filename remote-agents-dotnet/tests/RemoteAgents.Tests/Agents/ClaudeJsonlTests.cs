using RemoteAgents.Providers.Claude;

namespace RemoteAgents.Tests.Agents;

// Unit tests for ClaudeJsonl.TryReadLastAssistantText. Real Claude writes
// the file under ~/.claude/projects/<encoded>/<sessionId>.jsonl — these
// tests stage one there for a synthetic projectDir so we exercise the
// real PathFor resolution + ProviderJsonlIngestSink.EncodeCwd path.
public class ClaudeJsonlTests : IDisposable
{
    private readonly string _projectDir;
    private readonly string _sessionId;
    private readonly string _jsonlPath;

    public ClaudeJsonlTests()
    {
        // Synthetic "project dir" — doesn't need to exist on disk; only
        // its encoded form is used to compute the JSONL path.
        _projectDir = "C:\\fake\\ra-jsonl-tests-" + Guid.NewGuid().ToString("N");
        _sessionId = Guid.NewGuid().ToString();
        _jsonlPath = ClaudeJsonl.PathFor(_projectDir, _sessionId);
        Directory.CreateDirectory(Path.GetDirectoryName(_jsonlPath)!);
    }

    public void Dispose()
    {
        try { File.Delete(_jsonlPath); } catch { }
        try { Directory.Delete(Path.GetDirectoryName(_jsonlPath)!); } catch { }
    }

    [Fact]
    public void PathFor_uses_encoded_cwd_and_session_id()
    {
        var path = ClaudeJsonl.PathFor("C:\\Unity\\Card Framework", "abc-123");
        Assert.Contains("C--Unity-Card Framework", path);
        Assert.EndsWith("abc-123.jsonl", path);
    }

    [Fact]
    public void TryReadLastAssistantText_missing_file_returns_null()
    {
        var result = ClaudeJsonl.TryReadLastAssistantText(_projectDir, _sessionId);
        Assert.Null(result);
    }

    [Fact]
    public void TryReadLastAssistantText_returns_assistant_text_after_user_prompt()
    {
        File.WriteAllLines(_jsonlPath, new[]
        {
            UserLine("Hello, please summarize."),
            AssistantTextLine("Sure — here is a summary."),
        });

        var result = ClaudeJsonl.TryReadLastAssistantText(_projectDir, _sessionId, "Hello, please summarize.");
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

        var result = ClaudeJsonl.TryReadLastAssistantText(_projectDir, _sessionId, "Two parts please.");
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

        var result = ClaudeJsonl.TryReadLastAssistantText(_projectDir, _sessionId, "Run the thing.");
        Assert.Equal("OK, running.\nDone.", result);
        Assert.DoesNotContain("internal reasoning", result);
        Assert.DoesNotContain("Bash", result);
    }

    [Fact]
    public void TryReadLastAssistantText_with_prompt_hint_anchors_on_matching_user_message()
    {
        // Resume scenario: a prior turn's assistant text precedes this
        // turn's user prompt. We want only this turn's assistant text.
        File.WriteAllLines(_jsonlPath, new[]
        {
            UserLine("Previous turn."),
            AssistantTextLine("Previous answer."),
            UserLine("Fix the bug at line 42."),
            AssistantTextLine("Fixed."),
        });

        var result = ClaudeJsonl.TryReadLastAssistantText(_projectDir, _sessionId, "Fix the bug at line 42.");
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

        // No hint → anchor on the last user message regardless of content.
        var result = ClaudeJsonl.TryReadLastAssistantText(_projectDir, _sessionId, promptHint: null);
        Assert.Equal("Second answer.", result);
    }

    [Fact]
    public void TryReadLastAssistantText_ignores_internal_tool_result_user_messages()
    {
        // When Claude calls a tool, the result is recorded as a user message
        // with content.type=="tool_result". That should not become the
        // anchor if it doesn't match the promptHint.
        File.WriteAllLines(_jsonlPath, new[]
        {
            UserLine("Run tests please."),
            AssistantTextLine("Running them now."),
            UserToolResultLine("tests passed: 42"),
            AssistantTextLine("All 42 tests passed."),
        });

        var result = ClaudeJsonl.TryReadLastAssistantText(_projectDir, _sessionId, "Run tests please.");
        Assert.Equal("Running them now.\nAll 42 tests passed.", result);
    }

    [Fact]
    public void TryReadLastAssistantText_malformed_lines_are_skipped()
    {
        File.WriteAllLines(_jsonlPath, new[]
        {
            "this is not json",
            "",
            UserLine("Hello."),
            "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":\"not-an-array\"}}",
            AssistantTextLine("Hi."),
        });

        var result = ClaudeJsonl.TryReadLastAssistantText(_projectDir, _sessionId, "Hello.");
        // The malformed-content line: content is a string, so we treat it
        // as a single text block ("not-an-array"), followed by "Hi."
        Assert.Equal("not-an-array\nHi.", result);
    }

    [Fact]
    public void TryReadLastAssistantText_no_assistant_text_returns_empty_string()
    {
        File.WriteAllLines(_jsonlPath, new[]
        {
            UserLine("Just compile."),
            AssistantMixedLine(thinking: null, text: null, toolName: "Bash"),
        });

        var result = ClaudeJsonl.TryReadLastAssistantText(_projectDir, _sessionId, "Just compile.");
        Assert.Equal("", result);
    }

    // ── line builders ──────────────────────────────────────────────────

    private static string UserLine(string text)
        => $"{{\"type\":\"user\",\"message\":{{\"role\":\"user\",\"content\":[{{\"type\":\"text\",\"text\":{Quote(text)}}}]}}}}";

    private static string UserToolResultLine(string output)
        => $"{{\"type\":\"user\",\"message\":{{\"role\":\"user\",\"content\":[{{\"type\":\"tool_result\",\"content\":{Quote(output)}}}]}}}}";

    private static string AssistantTextLine(string text)
        => $"{{\"type\":\"assistant\",\"message\":{{\"role\":\"assistant\",\"content\":[{{\"type\":\"text\",\"text\":{Quote(text)}}}]}}}}";

    private static string AssistantMixedLine(string? thinking, string? text, string? toolName)
    {
        var blocks = new List<string>();
        if (thinking is not null)
            blocks.Add($"{{\"type\":\"thinking\",\"thinking\":{Quote(thinking)}}}");
        if (text is not null)
            blocks.Add($"{{\"type\":\"text\",\"text\":{Quote(text)}}}");
        if (toolName is not null)
            blocks.Add($"{{\"type\":\"tool_use\",\"name\":{Quote(toolName)},\"input\":{{}}}}");
        return $"{{\"type\":\"assistant\",\"message\":{{\"role\":\"assistant\",\"content\":[{string.Join(',', blocks)}]}}}}";
    }

    private static string Quote(string s)
        => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
