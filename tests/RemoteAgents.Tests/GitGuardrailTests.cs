using RemoteAgents.Domain.Git;

namespace RemoteAgents.Tests;

public class GitGuardrailTests
{
    [Theory]
    [InlineData("main")]
    [InlineData("master")]
    public async Task Push_force_to_protected_branch_is_refused(string branch)
    {
        var git = new Git(".");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Op.Exec(git.Push, new PushArgs(Branch: branch, Force: true)));
    }

    [Fact]
    public async Task Commit_with_empty_file_list_is_refused()
    {
        var git = new Git(".");
        await Assert.ThrowsAsync<ArgumentException>(
            () => Op.Exec(git.Commit, new CommitArgs("a message", Array.Empty<string>())));
    }

    [Fact]
    public async Task Commit_with_blank_message_is_refused()
    {
        var git = new Git(".");
        await Assert.ThrowsAsync<ArgumentException>(
            () => Op.Exec(git.Commit, new CommitArgs("   ", new[] { "a.txt" })));
    }
}
