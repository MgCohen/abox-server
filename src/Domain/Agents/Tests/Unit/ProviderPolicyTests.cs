using ABox.Domain.Agents;
using ABox.Domain.Agents.Codex;

namespace ABox.Agents.Tests.Unit;

public class ProviderPolicyTests
{
    private static readonly CodexSandbox DummyBox =
        new("abox-codex:latest", "abox-boxnet", "http://proxy:8888", new DirectoryInfo(Path.GetTempPath()));

    [Rule("Codex resume → reuses the prior session via bypass, without re-setting cd or sandbox")]
    [Fact]
    public void Codex_resume_omits_cd_and_sandbox_and_bypasses_instead()
    {
        var args = CodexProtocol.BuildArgs("sess-1", "C:/proj", "last.txt", "gpt-5.5");

        Assert.Contains("resume", args);
        Assert.DoesNotContain("--cd", args);
        Assert.DoesNotContain("--sandbox", args);
        Assert.Contains("--dangerously-bypass-approvals-and-sandbox", args);
    }

    [Rule("Codex new turn → sets cd and bypasses its own sandbox, since the box is the wall")]
    [Fact]
    public void Codex_new_turn_sets_cd_and_bypasses_its_own_sandbox()
    {
        var args = CodexProtocol.BuildArgs(null, "/work", "last.txt", "gpt-5.5");

        Assert.Contains("--cd", args);
        Assert.DoesNotContain("--sandbox", args);
        Assert.Contains("--dangerously-bypass-approvals-and-sandbox", args);
    }

    [Rule("Codex driven with a non-bypass policy → throws an actionable NotSupportedException naming the policy")]
    [Fact]
    public async Task Codex_rejects_a_non_bypass_policy_with_an_actionable_error()
    {
        var config = new CodexConfig("reviewer", "Reviews.", "gpt-5.5", "You review.")
        {
            Policy = PermissionPolicy.Ask,
        };
        var provider = new CodexProvider(config, DummyBox);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.DriveAsync(new AgentRunRequest("do it", "C:/proj"), CancellationToken.None));
        Assert.Contains("Ask", ex.Message);
    }

    [Rule("Directive composed with a role prompt → preserves the role text and appends the directive")]
    [Fact]
    public void The_directive_appends_to_a_role_system_prompt()
    {
        var composed = AgentDirective.ComposeSystemPrompt("You review.");

        Assert.Contains("You review.", composed);
        Assert.Contains(QuestionParser.Sentinel, composed);
        Assert.Contains("irreversible", composed);
    }

    [Rule("Directive composed with an empty role prompt → returns the standalone unattended directive")]
    [Fact]
    public void The_directive_stands_alone_when_there_is_no_role_prompt()
        => Assert.Equal(AgentDirective.Unattended, AgentDirective.ComposeSystemPrompt(""));
}
