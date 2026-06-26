using ABox.Domain.Agents;
using ABox.Domain.Agents.Claude;

namespace ABox.Agents.Tests.Unit;

public class DenyResolverTests
{
    [Rule("DenyResolver on a permission request → answers Deny")]
    [Fact]
    public async Task Refuses_a_permission_with_an_explicit_deny()
    {
        var resolver = new DenyResolver();
        var permission = new AgentQuestion.Choice("Allow `Bash`: rm -rf build ?", ["Allow", "Deny"], false, "raw");

        var answer = await resolver.ResolveAsync(permission, DecisionKind.Permission, CancellationToken.None);

        Assert.Equal("Deny", answer);
        Assert.False(ClaudePermission.IsAllow(answer));
    }

    [Rule("DenyResolver on an open question → abstains with null")]
    [Fact]
    public async Task Abstains_on_an_open_question()
    {
        var resolver = new DenyResolver();
        var question = new AgentQuestion.Open("Which bucket?", "raw");

        var answer = await resolver.ResolveAsync(question, DecisionKind.Question, CancellationToken.None);

        Assert.Null(answer);
    }
}
