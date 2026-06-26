using ABox.Domain.Flow;
using ABox.Domain.Git;
using ABox.Infrastructure.CommandLine;

namespace ABox.Git.Tests.E2E;

public class GitChoreFlowTests
{
    [Rule("chore on a dirty tree → committed and pushed, working copy clean")]
    [Fact]
    public async Task Chore_commits_working_changes_and_pushes_them_to_the_remote()
    {
        using var repo = await TempGitRepo.CreateWithRemoteAsync();
        await repo.WriteAsync("work.txt", "new work");

        await new GitChoreFlow().ExecuteAsync(
            new FlowConfig("chore", "chore"),
            new FlowContext("c", "c", repo.Path, "Add work"),
            CancellationToken.None);

        Assert.False((await Op.Exec(new DomainGit(repo.Path).Status, new StatusArgs(), repo.Path)).IsDirty);

        var remoteSubject = (await RunCommand.RunAsync(
                $"git --git-dir=\"{repo.RemotePath}\" log -1 --pretty=%s",
                new RunCommandOptions(Cwd: repo.Path)))
            .EnsureOk("git log").Stdout.Trim();
        Assert.Equal("Add work", remoteSubject);
    }
}
