using System.Text.Json;

namespace ABox.Governance.Hooks.Tests.Unit;

public sealed class GitInstallerTests
{
    [Rule("GitInstaller.InstallPostCommit on a default repo → writes an executable post-commit that calls abox-hooks commit")]
    [Fact]
    public void Installs_an_executable_post_commit_calling_the_cli()
    {
        var repo = NewRepo();
        try
        {
            var result = GitInstaller.InstallPostCommit(repo);

            Assert.True(result.Installed, result.Message);
            var postCommit = Path.Combine(repo, ".git", "hooks", "post-commit");
            Assert.Contains(GitInstaller.Marker, File.ReadAllText(postCommit));
            if (!OperatingSystem.IsWindows())
                Assert.True((File.GetUnixFileMode(postCommit) & UnixFileMode.UserExecute) != 0);
        }
        finally
        {
            SafeDelete(repo);
        }
    }

    [Rule("GitInstaller.InstallPostCommit on a repo with a custom core.hooksPath → refuses without writing a hook")]
    [Fact]
    public void Refuses_to_touch_a_managed_hooks_dir()
    {
        var repo = NewRepo();
        try
        {
            Git.Output(repo, "config", "core.hooksPath", ".githooks");

            var result = GitInstaller.InstallPostCommit(repo);

            Assert.False(result.Installed);
            Assert.Contains("core.hooksPath", result.Message);
            Assert.False(File.Exists(Path.Combine(repo, ".git", "hooks", "post-commit")));
        }
        finally
        {
            SafeDelete(repo);
        }
    }

    [Rule("abox-hooks commit in an opted-in repo → appends a CommitLanded line and dispatches matching hooks")]
    [Fact]
    public async Task Commit_emits_CommitLanded_and_dispatches()
    {
        var repo = NewRepo();
        try
        {
            Commit(repo, "first");
            var sha = Git.Output(repo, "rev-parse", "HEAD")!;
            Directory.CreateDirectory(Path.Combine(repo, ".abox"));
            var feat = Directory.CreateDirectory(Path.Combine(repo, "feat")).FullName;
            File.WriteAllText(Path.Combine(feat, "on-commit.hook"),
                "on: [CommitLanded]\nmode: notify\nrun: cat > got.json\n");

            var code = await Cli.RunAsync(["commit", "--repo", repo]);
            Assert.Equal(0, code);

            var line = File.ReadAllText(Path.Combine(repo, ".abox", "hooks.jsonl")).Trim();
            using var doc = JsonDocument.Parse(line);
            Assert.Equal("CommitLanded", doc.RootElement.GetProperty("kind").GetString());
            Assert.Equal("Git", doc.RootElement.GetProperty("source").GetString());
            Assert.Equal(sha, doc.RootElement.GetProperty("raw").GetProperty("sha").GetString());

            Assert.Contains(sha, File.ReadAllText(Path.Combine(feat, "got.json")));
        }
        finally
        {
            SafeDelete(repo);
        }
    }

    [Rule("abox-hooks commit with no .abox opt-in → emits nothing")]
    [Fact]
    public async Task Commit_is_a_noop_without_opt_in()
    {
        var repo = NewRepo();
        try
        {
            Commit(repo, "first");

            Assert.Equal(0, await Cli.RunAsync(["commit", "--repo", repo]));
            Assert.False(File.Exists(Path.Combine(repo, ".abox", "hooks.jsonl")));
        }
        finally
        {
            SafeDelete(repo);
        }
    }

    // git marks objects read-only, so a recursive delete throws on Windows; cleanup is
    // best-effort (the runner reaps temp anyway) — mirrors the repo's TempGitRepo.
    private static void SafeDelete(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private static string NewRepo()
    {
        var repo = Directory.CreateTempSubdirectory("hookgit-").FullName;
        Git.Output(repo, "init");
        Git.Output(repo, "config", "user.email", "t@t");
        Git.Output(repo, "config", "user.name", "t");
        Git.Output(repo, "config", "commit.gpgsign", "false");
        return repo;
    }

    private static void Commit(string repo, string message)
    {
        File.WriteAllText(Path.Combine(repo, $"{message}.txt"), message);
        Git.Output(repo, "add", "-A");
        Git.Output(repo, "commit", "-m", message);
    }
}
