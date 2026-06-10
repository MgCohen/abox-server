using RemoteAgents.Domain.Agents;
using RemoteAgents.Domain.Agents.Claude;

namespace RemoteAgents.Tests;

public class ClaudeProtocolTests
{
    [Theory]
    [InlineData(PermissionPolicy.Bypass, "bypassPermissions")]
    [InlineData(PermissionPolicy.Auto, "default")]
    [InlineData(PermissionPolicy.Ask, "default")]
    public void PermissionMode_maps_each_policy_to_its_claude_flag(PermissionPolicy policy, string expected)
        => Assert.Equal(expected, ClaudeProtocol.PermissionMode(policy));


    [Fact]
    public void BuildArgs_fresh_run_uses_session_id_not_resume()
    {
        var args = ClaudeProtocol.BuildArgs("sess-1", isResume: false, "acceptEdits", "", "");

        Assert.Equal(new[] { "--session-id", "sess-1" }, args.Take(2));
        Assert.DoesNotContain("--resume", args);
    }

    [Fact]
    public void BuildArgs_resumed_run_uses_resume_not_session_id()
    {
        var args = ClaudeProtocol.BuildArgs("sess-1", isResume: true, "acceptEdits", "", "");

        Assert.Equal(new[] { "--resume", "sess-1" }, args.Take(2));
        Assert.DoesNotContain("--session-id", args);
    }

    [Fact]
    public void BuildArgs_carries_permission_mode_model_and_system_prompt_file()
    {
        var args = ClaudeProtocol.BuildArgs("sess-1", isResume: false, "acceptEdits", "opus", "C:/tmp/sys.txt");

        AssertPair(args, "--permission-mode", "acceptEdits");
        AssertPair(args, "--model", "opus");
        AssertPair(args, "--append-system-prompt-file", "C:/tmp/sys.txt");
    }

    [Fact]
    public void BuildArgs_adds_the_settings_file_when_provided()
    {
        var args = ClaudeProtocol.BuildArgs("sess-1", isResume: false, "acceptEdits", "", null, "C:/tmp/ra-hooks.json");

        AssertPair(args, "--settings", "C:/tmp/ra-hooks.json");
    }

    [Fact]
    public void BuildArgs_omits_the_settings_file_by_default()
    {
        var args = ClaudeProtocol.BuildArgs("sess-1", isResume: false, "acceptEdits", "", null);

        Assert.DoesNotContain("--settings", args);
    }

    [Fact]
    public void BuildArgs_omits_optional_flags_when_blank()
    {
        var args = ClaudeProtocol.BuildArgs("sess-1", isResume: false, "", "", "");

        Assert.DoesNotContain("--permission-mode", args);
        Assert.DoesNotContain("--model", args);
        Assert.DoesNotContain("--append-system-prompt-file", args);
    }

    [Theory]
    [InlineData("⏵⏵ bypass permissions on (shift+tab to cycle)")]
    [InlineData("? for shortcuts · ← for agents")]
    public void IsPromptReady_recognizes_the_input_bar_in_either_permission_mode(string footer)
        => Assert.True(ClaudeProtocol.IsPromptReady(footer));

    [Fact]
    public void IsPromptReady_is_false_for_a_startup_dialog()
        => Assert.False(ClaudeProtocol.IsPromptReady("Is this a project you trust? Enter to confirm · Esc to cancel"));

    [Theory]
    [InlineData("Do you trust this folder?", StartupDialog.Trust)]
    [InlineData("Is this a project you want to open?", StartupDialog.Trust)]
    [InlineData("Bypass Permissions mode", StartupDialog.BypassWarning)]
    [InlineData("Yes, I accept the risk", StartupDialog.BypassWarning)]
    public void DetectStartupDialog_classifies_known_dialogs(string buffer, StartupDialog expected)
    {
        Assert.Equal(expected, ClaudeProtocol.DetectStartupDialog(buffer));
    }

    [Fact]
    public void DetectStartupDialog_returns_null_for_ordinary_output()
    {
        Assert.Null(ClaudeProtocol.DetectStartupDialog("Welcome to Claude Code"));
    }

    [Fact]
    public void DetectStartupDialog_matches_through_ansi_noise()
    {
        var buffer = "\x1b[1m\x1b[32mDo you \x1b[0mtrust this folder\x1b[0m?";
        Assert.Equal(StartupDialog.Trust, ClaudeProtocol.DetectStartupDialog(buffer));
    }

    private static void AssertPair(List<string> args, string flag, string value)
    {
        var i = args.IndexOf(flag);
        Assert.True(i >= 0 && i + 1 < args.Count, $"missing flag {flag}");
        Assert.Equal(value, args[i + 1]);
    }
}
