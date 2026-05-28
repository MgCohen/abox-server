using RemoteAgents.Primitives;

namespace RemoteAgents.Tests.Primitives;

public class GitOpsTests
{
    [Fact]
    public async Task PushAsync_force_to_main_is_refused()
    {
        var req = new GitPushRequest(ProjectDir: ".", Branch: "main", Force: true);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await GitOps.PushAsync(req));
    }

    [Fact]
    public async Task PushAsync_force_to_master_is_refused()
    {
        var req = new GitPushRequest(ProjectDir: ".", Branch: "master", Force: true);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await GitOps.PushAsync(req));
    }

    [Fact]
    public async Task AddAsync_with_empty_files_throws()
    {
        var req = new GitAddRequest(ProjectDir: ".", Files: Array.Empty<string>());
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await GitOps.AddAsync(req));
    }

    [Fact]
    public async Task CommitAsync_with_empty_message_throws()
    {
        var req = new GitCommitRequest(ProjectDir: ".", Message: "");
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await GitOps.CommitAsync(req));
    }
}
