using RemoteAgents.Actors.Agents;
using RemoteAgents.Actors.Agents.Claude;

namespace RemoteAgents.Tests;

public class InteractivityTests
{
    [Fact]
    public void Autonomous_composes_the_unattended_directive()
        => Assert.Equal(AgentDirective.Unattended,
            AgentDirective.ComposeSystemPrompt("", Interactivity.Autonomous));

    [Fact]
    public void Interactive_composes_a_distinct_ask_first_directive()
    {
        var composed = AgentDirective.ComposeSystemPrompt("", Interactivity.Interactive);

        Assert.Equal(AgentDirective.Interactive, composed);
        Assert.NotEqual(AgentDirective.Unattended, composed);
        Assert.Contains("INTERACTIVE mode", composed);
        Assert.Contains("ask rather than guess", composed);
    }

    [Fact]
    public void Both_directives_share_the_envelope_format_and_append_to_a_role_prompt()
    {
        foreach (var mode in new[] { Interactivity.Autonomous, Interactivity.Interactive })
        {
            var composed = AgentDirective.ComposeSystemPrompt("You review.", mode);
            Assert.StartsWith("You review.", composed);
            Assert.Contains(QuestionParser.Sentinel, composed);
        }
    }

    [Fact]
    public async Task The_auto_resolver_self_answers_and_records_the_assumption()
    {
        var resolver = new AutoResolver();
        var question = new AgentQuestion.Open("Which bucket?", "raw");

        var answer = await resolver.ResolveAsync(question, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(answer));
        var recorded = Assert.Single(resolver.Assumptions);
        Assert.Equal("Which bucket?", recorded.Question);
        Assert.Equal(answer, recorded.Answer);
    }

    // Autonomous + Ask degrades to deny: the self-answer is never "Allow".
    [Fact]
    public async Task The_auto_resolver_does_not_allow_a_permission_choice()
    {
        var resolver = new AutoResolver();
        var permission = new AgentQuestion.Choice("Allow `Bash`: rm -rf build ?", ["Allow", "Deny"], false, "raw");

        var answer = await resolver.ResolveAsync(permission, CancellationToken.None);

        Assert.False(ClaudePermission.IsAllow(answer));
    }
}
