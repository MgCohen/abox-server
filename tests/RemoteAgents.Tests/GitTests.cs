using RemoteAgents.Actors.Git;
using RemoteAgents.Flows;

namespace RemoteAgents.Tests;

public class GitTests
{
    private static FlowContext CtxFor(string dir) => new("test", "test", dir, "test");

    [Fact]
    public async Task ChangedFiles_reports_modified_and_untracked()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("a.txt", "hello");
        await repo.WriteAsync("b.txt", "world");
        await repo.CommitAllAsync("init");

        await repo.WriteAsync("a.txt", "changed");
        await repo.WriteAsync("c.txt", "new");

        var result = await new Git().ChangedFiles().Execute(CtxFor(repo.Path), CancellationToken.None);

        Assert.Contains("a.txt", result.Files);
        Assert.Contains("c.txt", result.Files);
        Assert.Equal(2, result.Files.Count);
    }

    [Fact]
    public async Task CheckDirty_true_when_changed_false_when_clean()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("a.txt", "v1");
        await repo.CommitAllAsync("init");

        var git = new Git();
        var ctx = CtxFor(repo.Path);
        Assert.False((await git.CheckDirty().Execute(ctx, CancellationToken.None)).IsDirty);

        await repo.WriteAsync("a.txt", "v2");
        Assert.True((await git.CheckDirty().Execute(ctx, CancellationToken.None)).IsDirty);
    }

    [Fact]
    public async Task Commit_stages_listed_files_and_returns_hash_and_subject()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("a.txt", "v1");
        await repo.CommitAllAsync("init");

        await repo.WriteAsync("a.txt", "v2");
        var ctx = CtxFor(repo.Path);
        var result = await new Git().Commit("Fix the thing", new[] { "a.txt" }, coAuthor: "Bot").Execute(ctx, CancellationToken.None);

        Assert.Equal(40, result.Hash.Length);
        Assert.Equal("Fix the thing", result.Subject);
        Assert.False((await new Git().CheckDirty().Execute(ctx, CancellationToken.None)).IsDirty);
    }

    [Fact]
    public async Task Diff_reports_file_count_and_text()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("a.txt", "v1");
        await repo.CommitAllAsync("init");

        await repo.WriteAsync("a.txt", "v2 changed");
        var result = await new Git().Diff().Execute(CtxFor(repo.Path), CancellationToken.None);

        Assert.Equal(1, result.Files);
        Assert.Contains("a.txt", result.Text);
    }

    [Fact]
    public async Task ChangedFiles_stable_after_checkout_dash_dash()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("a.txt", "v1");
        await repo.CommitAllAsync("init");

        await repo.WriteAsync("a.txt", "v2");
        await repo.RunAsync("git checkout -- a.txt");

        var result = await new Git().ChangedFiles().Execute(CtxFor(repo.Path), CancellationToken.None);
        Assert.Empty(result.Files);
    }
}
