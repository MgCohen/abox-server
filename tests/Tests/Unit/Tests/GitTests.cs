using ABox.Domain.Git;
using ABox.Infrastructure.CommandLine;

namespace ABox.Tests.Unit.Tests;

public class GitTests
{
    [Rule("RebaseOnto onto a rebuilt parent → replays the branch's commits onto the new base")]
    [Fact]
    public async Task RebaseOnto_replays_branch_onto_a_rebuilt_parent()
    {
        using var repo = await TempGitRepo.CreateAsync();
        await repo.WriteAsync("base.txt", "base");
        await repo.CommitAllAsync("base");

        await repo.RunAsync("git checkout -q -b phase1");
        await repo.WriteAsync("a.txt", "a1");
        await repo.CommitAllAsync("p1");
        var oldParent = await RevParse(repo, "phase1");

        await repo.RunAsync("git checkout -q -b phase2");
        await repo.WriteAsync("b.txt", "b1");
        await repo.CommitAllAsync("p2");

        await repo.RunAsync("git checkout -q phase1");
        await repo.WriteAsync("a.txt", "a1-rebuilt");
        await repo.RunAsync("git commit -aq --amend -m p1b");
        var newParent = await RevParse(repo, "phase1");

        await Op.Exec(new Git(repo.Path).RebaseOnto, new RebaseOntoArgs(newParent, oldParent, "phase2"));

        Assert.Equal(0, await ExitCode(repo, $"git merge-base --is-ancestor {newParent} phase2"));
        Assert.NotEqual(0, await ExitCode(repo, $"git merge-base --is-ancestor {oldParent} phase2"));
        Assert.Equal("b1", await repo.ReadAsync("b.txt"));
    }

    [Rule("Force push to a remote that advanced since the last fetch → refused before it can overwrite")]
    [Fact]
    public async Task Force_push_refuses_to_clobber_an_advanced_remote()
    {
        using var repo = await TempGitRepo.CreateWithRemoteAsync();
        await repo.RunAsync("git checkout -q -b feature");
        await repo.WriteAsync("f.txt", "v1");
        await repo.CommitAllAsync("f1");
        var git = new Git(repo.Path);
        await Op.Exec(git.Push, new PushArgs(Branch: "feature"));

        var clone = Path.Combine(Path.GetTempPath(), "ra-clone-" + Guid.NewGuid().ToString("N"));
        try
        {
            await RunCommand.RunAsync($"git clone -q \"{repo.RemotePath}\" \"{clone}\"", new RunCommandOptions());
            await RunCommand.RunAsync("git checkout -q feature", new RunCommandOptions(Cwd: clone));
            await File.WriteAllTextAsync(Path.Combine(clone, "f.txt"), "v2-remote");
            await RunCommand.RunAsync("git commit -aqm f2-remote", new RunCommandOptions(Cwd: clone));
            await RunCommand.RunAsync("git push -q origin feature", new RunCommandOptions(Cwd: clone));

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

    private static async Task<string> RevParse(TempGitRepo repo, string rev) =>
        (await RunCommand.RunAsync($"git rev-parse {rev}", new RunCommandOptions(Cwd: repo.Path))).Stdout.Trim();

    private static async Task<int> ExitCode(TempGitRepo repo, string cmd) =>
        (await RunCommand.RunAsync(cmd, new RunCommandOptions(Cwd: repo.Path))).ExitCode;

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

    [Rule("CheckDirty → reports whether the working tree has uncommitted changes")]
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
