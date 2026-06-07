using RemoteAgents.Actors.Agents;
using RemoteAgents.Actors.Agents.Codex;

namespace RemoteAgents.Tests;

public class ProviderPolicyTests
{
    [Fact]
    public void Codex_resume_omits_cd_and_sandbox_and_bypasses_instead()
    {
        var args = CodexProtocol.BuildArgs("sess-1", "C:/proj", "last.txt", "gpt-5.5", "read-only");

        Assert.Contains("resume", args);
        Assert.DoesNotContain("--cd", args);
        Assert.DoesNotContain("--sandbox", args);
        Assert.Contains("--dangerously-bypass-approvals-and-sandbox", args);
    }

    [Fact]
    public void Codex_new_turn_sets_cd_and_sandbox()
    {
        var args = CodexProtocol.BuildArgs(null, "C:/proj", "last.txt", "gpt-5.5", "read-only");

        Assert.Contains("--cd", args);
        Assert.Contains("--sandbox", args);
        Assert.DoesNotContain("--dangerously-bypass-approvals-and-sandbox", args);

        var sandbox = args[args.IndexOf("--sandbox") + 1];
        var expected = OperatingSystem.IsWindows() ? "danger-full-access" : "read-only";
        Assert.Equal(expected, sandbox);
    }

    [Fact]
    public async Task Codex_rejects_a_non_bypass_policy_with_an_actionable_error()
    {
        var config = new CodexConfig("reviewer", "Reviews.", "gpt-5.5", "You review.", Sandbox: "read-only")
        {
            Policy = PermissionPolicy.Ask,
        };
        var provider = new CodexProvider(config);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.DriveAsync(new AgentRunRequest("do it", "C:/proj"), CancellationToken.None));
        Assert.Contains("Ask", ex.Message);
    }

    [Fact]
    public void The_directive_appends_to_a_role_system_prompt()
    {
        var composed = AgentDirective.ComposeSystemPrompt("You review.");

        Assert.Contains("You review.", composed);
        Assert.Contains(QuestionParser.Sentinel, composed);
        Assert.Contains("irreversible", composed);
    }

    [Fact]
    public void The_directive_stands_alone_when_there_is_no_role_prompt()
        => Assert.Equal(AgentDirective.Unattended, AgentDirective.ComposeSystemPrompt(""));
}
