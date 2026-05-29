using RemoteAgents.Primitives;

namespace RemoteAgents.Tests.Primitives;

public class GitWorktreeTests
{
    [Fact]
    public async Task AddAsync_empty_path_throws()
    {
        var req = new GitWorktreeAddRequest(RepoDir: ".", Path: "", Branch: "x");
        await Assert.ThrowsAsync<ArgumentException>(() => GitWorktree.AddAsync(req));
    }

    [Fact]
    public async Task AddAsync_empty_branch_throws()
    {
        var req = new GitWorktreeAddRequest(RepoDir: ".", Path: "x", Branch: "");
        await Assert.ThrowsAsync<ArgumentException>(() => GitWorktree.AddAsync(req));
    }

    // Integration

    [Fact]
    public async Task Add_List_Remove_roundtrip()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("a.txt", "v1");
        await repo.CommitAllAsync("init");

        await GitOps.BranchCreateAsync(new GitBranchCreateRequest(repo.Path, "wt/slot1"));

        var wtPath = Path.Combine(Path.GetTempPath(), "ra-wt-" + Guid.NewGuid().ToString("N"));
        try
        {
            await GitWorktree.AddAsync(new GitWorktreeAddRequest(
                RepoDir: repo.Path,
                Path: wtPath,
                Branch: "wt/slot1"));

            var entries = await GitWorktree.ListAsync(repo.Path);
            // Primary checkout + new worktree.
            Assert.Equal(2, entries.Count);
            var slot = entries.FirstOrDefault(e => string.Equals(
                Path.GetFullPath(e.Path), Path.GetFullPath(wtPath), StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(slot);
            Assert.Equal("wt/slot1", slot!.Branch);
            Assert.NotEmpty(slot.Head);
            Assert.Null(slot.Locked);

            await GitWorktree.RemoveAsync(new GitWorktreeRemoveRequest(repo.Path, wtPath));
            var afterRemove = await GitWorktree.ListAsync(repo.Path);
            Assert.Single(afterRemove);
        }
        finally
        {
            try { if (Directory.Exists(wtPath)) Directory.Delete(wtPath, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task AddAsync_create_branch_inline()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("a.txt", "v1");
        await repo.CommitAllAsync("init");

        var wtPath = Path.Combine(Path.GetTempPath(), "ra-wt-" + Guid.NewGuid().ToString("N"));
        try
        {
            await GitWorktree.AddAsync(new GitWorktreeAddRequest(
                RepoDir: repo.Path,
                Path: wtPath,
                Branch: "wt/freshly-created",
                CreateBranch: true));

            var entries = await GitWorktree.ListAsync(repo.Path);
            Assert.Contains(entries, e => e.Branch == "wt/freshly-created");
        }
        finally
        {
            try { if (Directory.Exists(wtPath)) Directory.Delete(wtPath, recursive: true); } catch { }
        }
    }
}
