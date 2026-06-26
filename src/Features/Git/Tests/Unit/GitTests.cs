using ABox.Domain.Git;
using ABox.Infrastructure.CommandLine;

namespace ABox.Git.Tests.Unit;

public class GitTests
{
    [Rule("Force push to a remote that advanced since the last fetch → refused before it can overwrite")]
    [Fact]
    public async Task Force_push_refuses_to_clobber_an_advanced_remote()
    {
        using var repo = await TempGitRepo.CreateWithRemoteAsync();
        await repo.RunAsync("git checkout -q -b feature");
        await repo.WriteAsync("f.txt", "v1");
        await repo.CommitAllAsync("f1");
        var git = new DomainGit(repo.Path);
        await Op.Exec(git.Push, new PushArgs(Branch: "feature"));

        var clone = Path.Combine(Path.GetTempPath(), "ra-clone-" + Guid.NewGuid().ToString("N"));
        try
        {
            var inClone = new RunCommandOptions(Cwd: clone);
            (await RunCommand.RunAsync($"git clone -q \"{repo.RemotePath}\" \"{clone}\"", new RunCommandOptions())).EnsureOk("git clone");
            // A fresh clone has no committer identity, and CI runners set none globally; configure it here or the
            // advancing commit fails silently and the remote never moves, leaving the force push a harmless fast-forward.
            (await RunCommand.RunAsync("git config user.email advancer@example.com", inClone)).EnsureOk("git config email");
            (await RunCommand.RunAsync("git config user.name advancer", inClone)).EnsureOk("git config name");
            (await RunCommand.RunAsync("git checkout -q feature", inClone)).EnsureOk("git checkout");
            await File.WriteAllTextAsync(Path.Combine(clone, "f.txt"), "v2-remote");
            (await RunCommand.RunAsync("git commit -aqm f2-remote", inClone)).EnsureOk("git commit");
            (await RunCommand.RunAsync("git push -q origin feature", inClone)).EnsureOk("git push");

            await repo.WriteAsync("f.txt", "v2-local");
            await repo.CommitAllAsync("f2-local");

            await Assert.ThrowsAnyAsync<Exception>(
                () => Op.Exec(git.Push, new PushArgs(Branch: "feature", Force: true)));
        }
        finally
        {
            try { Directory.Delete(clone, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    [Rule("Status on a dirty tree → Paths lists each modified and untracked path")]
    [Fact]
    public async Task Status_lists_modified_and_untracked_paths()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("a.txt", "hello");
        await repo.WriteAsync("b.txt", "world");
        await repo.CommitAllAsync("init");

        await repo.WriteAsync("a.txt", "changed");
        await repo.WriteAsync("c.txt", "new");

        var result = await Op.Exec(new DomainGit(repo.Path).Status, new StatusArgs());

        Assert.Contains("a.txt", result.Paths);
        Assert.Contains("c.txt", result.Paths);
        Assert.Equal(2, result.Paths.Count);
    }

    [Rule("Status → IsDirty reports whether the working tree has uncommitted changes")]
    [Fact]
    public async Task Status_reports_dirty_when_changed_and_clean_when_not()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("a.txt", "v1");
        await repo.CommitAllAsync("init");

        var git = new DomainGit(repo.Path);
        Assert.False((await Op.Exec(git.Status, new StatusArgs())).IsDirty);

        await repo.WriteAsync("a.txt", "v2");
        Assert.True((await Op.Exec(git.Status, new StatusArgs())).IsDirty);
    }

    [Rule("Commit of listed files → stages and commits them, returning the full hash and subject and leaving the tree clean")]
    [Fact]
    public async Task Commit_stages_listed_files_and_returns_hash_and_subject()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("a.txt", "v1");
        await repo.CommitAllAsync("init");

        await repo.WriteAsync("a.txt", "v2");
        var git = new DomainGit(repo.Path);
        var result = await Op.Exec(git.Commit, new CommitArgs("Fix the thing", new[] { "a.txt" }, CoAuthor: "Bot"));

        Assert.Equal(40, result.Hash.Length);
        Assert.Equal("Fix the thing", result.Subject);
        Assert.False((await Op.Exec(git.Status, new StatusArgs())).IsDirty);
    }

    [Rule("Diff on a dirty tree → reports the changed-file count and the diff text naming each file")]
    [Fact]
    public async Task Diff_reports_file_count_and_text()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("a.txt", "v1");
        await repo.CommitAllAsync("init");

        await repo.WriteAsync("a.txt", "v2 changed");
        var result = await Op.Exec(new DomainGit(repo.Path).Diff, new DiffArgs());

        Assert.Equal(1, result.Files);
        Assert.Contains("a.txt", result.Text);
    }

    [Rule("Status after a reverting checkout → reports a clean tree")]
    [Fact]
    public async Task Status_clean_after_checkout_dash_dash()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("a.txt", "v1");
        await repo.CommitAllAsync("init");

        await repo.WriteAsync("a.txt", "v2");
        await repo.RunAsync("git checkout -- a.txt");

        var result = await Op.Exec(new DomainGit(repo.Path).Status, new StatusArgs());
        Assert.False(result.IsDirty);
        Assert.Empty(result.Paths);
    }
}
