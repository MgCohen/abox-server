using System.Text.Json;
using ABox.Domain.Agents;
using ABox.Domain.Agents.Claude;

namespace ABox.Tests.Unit.Tests;

public class ClaudePermissionTests
{
    [Fact]
    public void ToQuestion_builds_an_allow_deny_choice_naming_the_tool_and_command()
    {
        var payload = "{\"tool_name\":\"Bash\",\"tool_input\":{\"command\":\"rm -rf build/\"}}";
        var question = ClaudePermission.ToQuestion(new PermissionRequest("1", payload));

        Assert.Equal(new[] { "Allow", "Deny" }, question.Options);
        Assert.False(question.AllowFreeText);
        Assert.Contains("Bash", question.Prompt);
        Assert.Contains("rm -rf build/", question.Prompt);
        Assert.Equal(payload, question.RawTail);
    }

    [Fact]
    public void ToQuestion_uses_the_file_path_for_write_and_edit_tools()
    {
        var payload = "{\"tool_name\":\"Write\",\"tool_input\":{\"file_path\":\"C:/proj/secret.env\"}}";
        var question = ClaudePermission.ToQuestion(new PermissionRequest("1", payload));

        Assert.Contains("Write", question.Prompt);
        Assert.Contains("secret.env", question.Prompt);
    }

    [Fact]
    public void ToQuestion_falls_back_to_the_bare_tool_when_payload_is_unparseable()
    {
        var question = ClaudePermission.ToQuestion(new PermissionRequest("1", "not json"));

        Assert.Contains("tool", question.Prompt);
        Assert.Equal(new[] { "Allow", "Deny" }, question.Options);
    }

    [Fact]
    public void Describe_returns_the_full_untruncated_command_for_the_guardrail()
    {
        var command = "echo " + new string('x', 500) + " && rm -rf /";
        var payload = $"{{\"tool_name\":\"Bash\",\"tool_input\":{{\"command\":{System.Text.Json.JsonSerializer.Serialize(command)}}}}}";

        var (tool, detail) = ClaudePermission.Describe(payload);

        Assert.Equal("Bash", tool);
        Assert.Equal(command, detail);
    }

    [Fact]
    public void ToQuestion_truncates_a_long_command_for_display()
    {
        var command = new string('x', 500);
        var payload = $"{{\"tool_name\":\"Bash\",\"tool_input\":{{\"command\":{System.Text.Json.JsonSerializer.Serialize(command)}}}}}";

        var question = ClaudePermission.ToQuestion(new PermissionRequest("1", payload));

        Assert.Contains("…", question.Prompt);
        Assert.True(question.Prompt.Length < command.Length);
    }

    [Theory]
    [InlineData("Allow", true)]
    [InlineData("allow", true)]
    [InlineData(" Allow ", true)]
    [InlineData("Deny", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("anything else", false)]
    public void IsAllow_only_accepts_the_allow_answer(string? answer, bool expected)
        => Assert.Equal(expected, ClaudePermission.IsAllow(answer));

    [Fact]
    public void RenderResponse_emits_the_hook_specific_allow_envelope()
    {
        using var doc = JsonDocument.Parse(ClaudePermission.RenderResponse(allow: true, "approved"));
        var output = doc.RootElement.GetProperty("hookSpecificOutput");

        Assert.Equal("PreToolUse", output.GetProperty("hookEventName").GetString());
        Assert.Equal("allow", output.GetProperty("permissionDecision").GetString());
        Assert.Equal("approved", output.GetProperty("permissionDecisionReason").GetString());
    }

    [Fact]
    public void RenderResponse_emits_deny_for_a_refused_call()
    {
        using var doc = JsonDocument.Parse(ClaudePermission.RenderResponse(allow: false, "denied"));
        Assert.Equal("deny",
            doc.RootElement.GetProperty("hookSpecificOutput").GetProperty("permissionDecision").GetString());
    }
}
