using ABox.Domain.Agents;
using ABox.Domain.Agents.Claude;

namespace ABox.Tests.Unit.Tests;

public class InteractivityTests
{
    [Rule("ComposeSystemPrompt in Auto → emits the unattended directive")]
    [Fact]
    public void Auto_composes_the_unattended_directive()
        => Assert.Equal(AgentDirective.Unattended,
            AgentDirective.ComposeSystemPrompt("", Resolution.Auto));

    [Rule("ComposeSystemPrompt in Human → emits a distinct interactive ask-first directive")]
    [Fact]
    public void Human_composes_a_distinct_ask_first_directive()
    {
        var composed = AgentDirective.ComposeSystemPrompt("", Resolution.Human);

        Assert.Equal(AgentDirective.Interactive, composed);
        Assert.NotEqual(AgentDirective.Unattended, composed);
        Assert.Contains("INTERACTIVE mode", composed);
        Assert.Contains("ask rather than guess", composed);
    }

    [Rule("ComposeSystemPrompt in either mode → appends the directive after the role prompt and includes the envelope sentinel")]
    [Fact]
    public void Both_directives_share_the_envelope_format_and_append_to_a_role_prompt()
    {
        foreach (var mode in new[] { Resolution.Auto, Resolution.Human })
        {
            var composed = AgentDirective.ComposeSystemPrompt("You review.", mode);
            Assert.StartsWith("You review.", composed);
            Assert.Contains(QuestionParser.Sentinel, composed);
        }
    }

    [Rule("AutoResolver on an open question → self-answers a non-empty proceed instruction tagged with the Auto source")]
    [Fact]
    public async Task The_auto_resolver_self_answers_with_a_proceed_instruction()
    {
        var resolver = new AutoResolver();
        var question = new AgentQuestion.Open("Which bucket?", "raw");

        var answer = await resolver.ResolveAsync(question, DecisionKind.Question, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(answer));
        Assert.Equal(Resolution.Auto, resolver.Source);
    }

    // Auto + Ask degrades to deny: the self-answer is never "Allow".
    [Rule("AutoResolver on a permission choice → never self-answers Allow")]
    [Fact]
    public async Task The_auto_resolver_does_not_allow_a_permission_choice()
    {
        var resolver = new AutoResolver();
        var permission = new AgentQuestion.Choice("Allow `Bash`: rm -rf build ?", ["Allow", "Deny"], false, "raw");

        var answer = await resolver.ResolveAsync(permission, DecisionKind.Permission, CancellationToken.None);

        Assert.False(ClaudePermission.IsAllow(answer));
    }
}
