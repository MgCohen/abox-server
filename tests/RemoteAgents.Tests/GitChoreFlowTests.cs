using RemoteAgents.Domain.Flow;
using RemoteAgents.Domain.Git;
using RemoteAgents.Infrastructure.CommandLine;

namespace RemoteAgents.Tests;

public class GitChoreFlowTests
{
    [Fact]
    public async Task Chore_commits_working_changes_and_pushes_them_to_the_remote()
    {
        using var repo = await TempGitRepo.CreateWithRemoteAsync();
        await repo.WriteAsync("work.txt", "new work");

        await new GitChoreFlow().ExecuteAsync(
            new FlowConfig("chore", "chore"),
            new FlowContext("c", "c", repo.Path, "Add work"),
            CancellationToken.None);

        Assert.False((await Op.Exec(new Git(repo.Path).CheckDirty, new DirtyArgs(), repo.Path)).IsDirty);

        var remoteSubject = (await RunCommand.RunAsync(
                $"git --git-dir=\"{repo.RemotePath}\" log -1 --pretty=%s",
                new RunCommandOptions(Cwd: repo.Path)))
            .EnsureOk("git log").Stdout.Trim();
        Assert.Equal("Add work", remoteSubject);
    }
}
