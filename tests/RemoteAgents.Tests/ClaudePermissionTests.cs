using System.Text.Json;
using RemoteAgents.Actors.Agents;
using RemoteAgents.Actors.Agents.Claude;

namespace RemoteAgents.Tests;

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
