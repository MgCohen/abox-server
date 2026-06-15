using ABox.Domain.Git;

namespace ABox.Tests.Unit.Tests;

public class GitTests
{
    [Rule("ChangedFiles on a dirty tree → returns each modified and untracked path")]
    [Fact]
    public async Task ChangedFiles_reports_modified_and_untracked()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("a.txt", "hello");
        await repo.WriteAsync("b.txt", "world");
        await repo.CommitAllAsync("init");

        await repo.WriteAsync("a.txt", "changed");
        await repo.WriteAsync("c.txt", "new");

        var result = await Op.Exec(new Git(repo.Path).ChangedFiles, new ChangedFilesArgs());

        Assert.Contains("a.txt", result.Files);
        Assert.Contains("c.txt", result.Files);
        Assert.Equal(2, result.Files.Count);
    }

    [Rule("CheckDirty → IsDirty true when the tree has changes, false when clean")]
    [Fact]
    public async Task CheckDirty_true_when_changed_false_when_clean()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("a.txt", "v1");
        await repo.CommitAllAsync("init");

        var git = new Git(repo.Path);
        Assert.False((await Op.Exec(git.CheckDirty, new DirtyArgs())).IsDirty);

        await repo.WriteAsync("a.txt", "v2");
        Assert.True((await Op.Exec(git.CheckDirty, new DirtyArgs())).IsDirty);
    }

    [Rule("Commit of listed files → stages and commits them, returning the full hash and subject and leaving the tree clean")]
    [Fact]
    public async Task Commit_stages_listed_files_and_returns_hash_and_subject()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("a.txt", "v1");
        await repo.CommitAllAsync("init");

        await repo.WriteAsync("a.txt", "v2");
        var git = new Git(repo.Path);
        var result = await Op.Exec(git.Commit, new CommitArgs("Fix the thing", new[] { "a.txt" }, CoAuthor: "Bot"));

        Assert.Equal(40, result.Hash.Length);
        Assert.Equal("Fix the thing", result.Subject);
        Assert.False((await Op.Exec(git.CheckDirty, new DirtyArgs())).IsDirty);
    }

    [Rule("Diff on a dirty tree → reports the changed-file count and the diff text naming each file")]
    [Fact]
    public async Task Diff_reports_file_count_and_text()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("a.txt", "v1");
        await repo.CommitAllAsync("init");

        await repo.WriteAsync("a.txt", "v2 changed");
        var result = await Op.Exec(new Git(repo.Path).Diff, new DiffArgs());

        Assert.Equal(1, result.Files);
        Assert.Contains("a.txt", result.Text);
    }

    [Rule("ChangedFiles after a reverting checkout → reports no changes")]
    [Fact]
    public async Task ChangedFiles_stable_after_checkout_dash_dash()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("a.txt", "v1");
        await repo.CommitAllAsync("init");

        await repo.WriteAsync("a.txt", "v2");
        await repo.RunAsync("git checkout -- a.txt");

        var result = await Op.Exec(new Git(repo.Path).ChangedFiles, new ChangedFilesArgs());
        Assert.Empty(result.Files);
    }
}
