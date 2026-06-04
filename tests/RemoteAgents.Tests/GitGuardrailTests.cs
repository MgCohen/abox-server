using RemoteAgents.Actors.Git;
using RemoteAgents.Flows;

namespace RemoteAgents.Tests;

public class GitGuardrailTests
{
    private static readonly FlowContext Ctx = new("test", "test", ".", "test");

    [Theory]
    [InlineData("main")]
    [InlineData("master")]
    public async Task Push_force_to_protected_branch_is_refused(string branch)
    {
        var op = new Git().Push(branch: branch, force: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => op.Execute(Ctx, CancellationToken.None));
    }

    [Fact]
    public async Task Commit_with_empty_file_list_is_refused()
    {
        var op = new Git().Commit("a message", Array.Empty<string>());
        await Assert.ThrowsAsync<ArgumentException>(() => op.Execute(Ctx, CancellationToken.None));
    }

    [Fact]
    public async Task Commit_with_blank_message_is_refused()
    {
        var op = new Git().Commit("   ", new[] { "a.txt" });
        await Assert.ThrowsAsync<ArgumentException>(() => op.Execute(Ctx, CancellationToken.None));
    }
}
