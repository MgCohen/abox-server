using ABox.Domain.Agents;
using ABox.Domain.Agents.Claude;

namespace ABox.Tests.Unit.Tests;

public class ClaudeProtocolTests
{
    [Rule("PermissionMode given a permission policy → the matching Claude permission-mode flag value")]
    [Theory]
    [InlineData(PermissionPolicy.Bypass, "bypassPermissions")]
    [InlineData(PermissionPolicy.Auto, "default")]
    [InlineData(PermissionPolicy.Ask, "default")]
    public void PermissionMode_maps_each_policy_to_its_claude_flag(PermissionPolicy policy, string expected)
        => Assert.Equal(expected, ClaudeProtocol.PermissionMode(policy));


    [Rule("BuildArgs for a fresh run → leads with --session-id and omits --resume")]
    [Fact]
    public void BuildArgs_fresh_run_uses_session_id_not_resume()
    {
        var args = ClaudeProtocol.BuildArgs("sess-1", isResume: false, "acceptEdits", "", "");

        Assert.Equal(new[] { "--session-id", "sess-1" }, args.Take(2));
        Assert.DoesNotContain("--resume", args);
    }

    [Rule("BuildArgs for a resumed run → leads with --resume and omits --session-id")]
    [Fact]
    public void BuildArgs_resumed_run_uses_resume_not_session_id()
    {
        var args = ClaudeProtocol.BuildArgs("sess-1", isResume: true, "acceptEdits", "", "");

        Assert.Equal(new[] { "--resume", "sess-1" }, args.Take(2));
        Assert.DoesNotContain("--session-id", args);
    }

    [Rule("BuildArgs with permission mode, model, and system-prompt file → each as its paired flag and value")]
    [Fact]
    public void BuildArgs_carries_permission_mode_model_and_system_prompt_file()
    {
        var args = ClaudeProtocol.BuildArgs("sess-1", isResume: false, "acceptEdits", "opus", "C:/tmp/sys.txt");

        AssertPair(args, "--permission-mode", "acceptEdits");
        AssertPair(args, "--model", "opus");
        AssertPair(args, "--append-system-prompt-file", "C:/tmp/sys.txt");
    }

    [Rule("BuildArgs with a settings file → emits --settings paired with that path")]
    [Fact]
    public void BuildArgs_adds_the_settings_file_when_provided()
    {
        var args = ClaudeProtocol.BuildArgs("sess-1", isResume: false, "acceptEdits", "", null, "C:/tmp/ra-hooks.json");

        AssertPair(args, "--settings", "C:/tmp/ra-hooks.json");
    }

    [Rule("BuildArgs with no settings file → omits --settings entirely")]
    [Fact]
    public void BuildArgs_omits_the_settings_file_by_default()
    {
        var args = ClaudeProtocol.BuildArgs("sess-1", isResume: false, "acceptEdits", "", null);

        Assert.DoesNotContain("--settings", args);
    }

    [Rule("BuildArgs with blank optional fields → omits --permission-mode, --model, and --append-system-prompt-file")]
    [Fact]
    public void BuildArgs_omits_optional_flags_when_blank()
    {
        var args = ClaudeProtocol.BuildArgs("sess-1", isResume: false, "", "", "");

        Assert.DoesNotContain("--permission-mode", args);
        Assert.DoesNotContain("--model", args);
        Assert.DoesNotContain("--append-system-prompt-file", args);
    }

    [Rule("IsPromptReady given the input-bar footer in any permission mode → true")]
    [Theory]
    [InlineData("⏵⏵ bypass permissions on (shift+tab to cycle)")]
    [InlineData("? for shortcuts · ← for agents")]
    public void IsPromptReady_recognizes_the_input_bar_in_either_permission_mode(string footer)
        => Assert.True(ClaudeProtocol.IsPromptReady(footer));

    [Rule("IsPromptReady given a startup-dialog screen → false")]
    [Fact]
    public void IsPromptReady_is_false_for_a_startup_dialog()
        => Assert.False(ClaudeProtocol.IsPromptReady("Is this a project you trust? Enter to confirm · Esc to cancel"));

    [Rule("DetectStartupDialog given a known dialog's text → its StartupDialog classification")]
    [Theory]
    [InlineData("Do you trust this folder?", StartupDialog.Trust)]
    [InlineData("Is this a project you want to open?", StartupDialog.Trust)]
    [InlineData("Bypass Permissions mode", StartupDialog.BypassWarning)]
    [InlineData("Yes, I accept the risk", StartupDialog.BypassWarning)]
    public void DetectStartupDialog_classifies_known_dialogs(string buffer, StartupDialog expected)
    {
        Assert.Equal(expected, ClaudeProtocol.DetectStartupDialog(buffer));
    }

    [Rule("DetectStartupDialog given ordinary output → null")]
    [Fact]
    public void DetectStartupDialog_returns_null_for_ordinary_output()
    {
        Assert.Null(ClaudeProtocol.DetectStartupDialog("Welcome to Claude Code"));
    }

    [Rule("DetectStartupDialog given dialog text split by ANSI escapes → still classifies it")]
    [Fact]
    public void DetectStartupDialog_matches_through_ansi_noise()
    {
        var buffer = "\x1b[1m\x1b[32mDo you \x1b[0mtrust this folder\x1b[0m?";
        Assert.Equal(StartupDialog.Trust, ClaudeProtocol.DetectStartupDialog(buffer));
    }

    [Rule("BuildCredentialLauncher → reads the OAuth token from the mount file in-box and never embeds the token value")]
    [Fact]
    public void BuildCredentialLauncher_reads_the_token_from_the_mount_file()
    {
        var script = ClaudeProtocol.BuildCredentialLauncher("/session/credential");

        Assert.Contains("CLAUDE_CODE_OAUTH_TOKEN=\"$(cat /session/credential)\"", script);
        Assert.Contains("exec claude \"$@\"", script);
        Assert.DoesNotContain("ANTHROPIC_API_KEY", script);
    }

    [Rule("BuildBoxEnv never carries the credential → the token never reaches the PTY-echoed exec line")]
    [Fact]
    public void BuildBoxEnv_never_carries_the_credential()
    {
        var env = ClaudeProtocol.BuildBoxEnv("/home/box", "/session/stop-signal.json", "/session/perms", "http://proxy:8888");

        Assert.False(env.ContainsKey("CLAUDE_CODE_OAUTH_TOKEN"));
        Assert.False(env.ContainsKey("ANTHROPIC_API_KEY"));
    }

    [Rule("BuildBoxEnv with an egress proxy → routes the box out through HTTPS_PROXY and HTTP_PROXY")]
    [Fact]
    public void BuildBoxEnv_routes_through_the_proxy_when_set()
    {
        var env = ClaudeProtocol.BuildBoxEnv("/home/box", "/session/stop-signal.json", null, "http://proxy:8888");

        Assert.Equal("http://proxy:8888", env["HTTPS_PROXY"]);
        Assert.Equal("http://proxy:8888", env["HTTP_PROXY"]);
    }

    private static void AssertPair(List<string> args, string flag, string value)
    {
        var i = args.IndexOf(flag);
        Assert.True(i >= 0 && i + 1 < args.Count, $"missing flag {flag}");
        Assert.Equal(value, args[i + 1]);
    }
}
