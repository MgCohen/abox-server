using RemoteAgents.Actors.Agents.Claude;

namespace RemoteAgents.Tests;

public class ClaudeProtocolTests
{
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
    public void BuildArgs_omits_optional_flags_when_blank()
    {
        var args = ClaudeProtocol.BuildArgs("sess-1", isResume: false, "", "", "");

        Assert.DoesNotContain("--permission-mode", args);
        Assert.DoesNotContain("--model", args);
        Assert.DoesNotContain("--append-system-prompt-file", args);
    }

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
