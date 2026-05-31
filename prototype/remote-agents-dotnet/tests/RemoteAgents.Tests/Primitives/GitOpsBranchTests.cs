using RemoteAgents.Primitives;

namespace RemoteAgents.Tests.Primitives;

// Covers the verbs added alongside Track B prep: fetch/pull/checkout/
// branch/merge/rebase/log. Validation-only tests live next to write
// ops; integration uses TempGitRepo (also from GitOpsTests).
public class GitOpsBranchTests
{
    [Fact]
    public async Task PullAsync_ff_only_and_rebase_are_mutually_exclusive()
    {
        var req = new GitPullRequest(ProjectDir: ".", FfOnly: true, Rebase: true);
        await Assert.ThrowsAsync<ArgumentException>(() => GitOps.PullAsync(req));
    }

    [Fact]
    public async Task CheckoutAsync_empty_ref_throws()
    {
        var req = new GitCheckoutRequest(ProjectDir: ".", Ref: "");
        await Assert.ThrowsAsync<ArgumentException>(() => GitOps.CheckoutAsync(req));
    }

    [Fact]
    public async Task BranchCreateAsync_empty_name_throws()
    {
        var req = new GitBranchCreateRequest(ProjectDir: ".", Name: "");
        await Assert.ThrowsAsync<ArgumentException>(() => GitOps.BranchCreateAsync(req));
    }

    [Theory, InlineData("main"), InlineData("master")]
    public async Task BranchDeleteAsync_refuses_main_master(string name)
    {
        var req = new GitBranchDeleteRequest(ProjectDir: ".", Name: name);
        await Assert.ThrowsAsync<InvalidOperationException>(() => GitOps.BranchDeleteAsync(req));
    }

    [Fact]
    public async Task MergeAsync_ff_only_and_no_ff_are_mutually_exclusive()
    {
        var req = new GitMergeRequest(ProjectDir: ".", Ref: "x", FfOnly: true, NoFf: true);
        await Assert.ThrowsAsync<ArgumentException>(() => GitOps.MergeAsync(req));
    }

    [Fact]
    public async Task RebaseAsync_empty_upstream_throws()
    {
        var req = new GitRebaseRequest(ProjectDir: ".", Upstream: "");
        await Assert.ThrowsAsync<ArgumentException>(() => GitOps.RebaseAsync(req));
    }

    // Integration

    [Fact]
    public async Task BranchCreate_Checkout_List_roundtrip()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("a.txt", "v1");
        await repo.CommitAllAsync("init");

        await GitOps.BranchCreateAsync(new GitBranchCreateRequest(repo.Path, "feature/x"));
        var branches = await GitOps.BranchListAsync(repo.Path);
        Assert.Contains("feature/x", branches);

        await GitOps.CheckoutAsync(new GitCheckoutRequest(repo.Path, "feature/x"));
        var current = await GitOps.CurrentBranchAsync(repo.Path);
        Assert.Equal("feature/x", current);
    }

    [Fact]
    public async Task CheckoutAsync_create_branch_from_ref()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("a.txt", "v1");
        await repo.CommitAllAsync("init");

        await GitOps.CheckoutAsync(new GitCheckoutRequest(
            repo.Path, "feature/y", CreateBranch: true));
        Assert.Equal("feature/y", await GitOps.CurrentBranchAsync(repo.Path));
    }

    [Fact]
    public async Task BranchDeleteAsync_removes_branch()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("a.txt", "v1");
        await repo.CommitAllAsync("init");

        await GitOps.BranchCreateAsync(new GitBranchCreateRequest(repo.Path, "scratch"));
        await GitOps.BranchDeleteAsync(new GitBranchDeleteRequest(repo.Path, "scratch", Force: true));
        Assert.DoesNotContain("scratch", await GitOps.BranchListAsync(repo.Path));
    }

    [Fact]
    public async Task LogAsync_returns_hash_and_subject()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("a.txt", "v1");
        await repo.CommitAllAsync("first commit");
        await repo.WriteAsync("a.txt", "v2");
        await repo.CommitAllAsync("second commit");

        var entries = await GitOps.LogAsync(new GitLogRequest(repo.Path, MaxCount: 5));
        Assert.Equal(2, entries.Count);
        Assert.Equal("second commit", entries[0].Subject);
        Assert.Equal("first commit", entries[1].Subject);
        Assert.NotEmpty(entries[0].Hash);
    }

    [Fact]
    public async Task MergeAsync_ff_only_fast_forwards()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("a.txt", "v1");
        await repo.CommitAllAsync("init");

        var baseBranch = await GitOps.CurrentBranchAsync(repo.Path);
        await GitOps.CheckoutAsync(new GitCheckoutRequest(repo.Path, "feature/m", CreateBranch: true));
        await repo.WriteAsync("a.txt", "v2");
        await repo.CommitAllAsync("on feature");

        await GitOps.CheckoutAsync(new GitCheckoutRequest(repo.Path, baseBranch));
        await GitOps.MergeAsync(new GitMergeRequest(repo.Path, "feature/m", FfOnly: true));

        Assert.Equal("v2", await repo.ReadAsync("a.txt"));
    }
}
