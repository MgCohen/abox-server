using RemoteAgents.Infrastructure.CommandLine;

namespace RemoteAgents.Tests;

internal sealed class TempGitRepo : IDisposable
{
    public string Path { get; }
    public string? RemotePath { get; private set; }

    private TempGitRepo(string path) => Path = path;

    public static async Task<TempGitRepo> CreateAsync()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ra-git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var repo = new TempGitRepo(dir);
        await repo.RunAsync("git init -q");
        await repo.RunAsync("git config user.email tests@example.com");
        await repo.RunAsync("git config user.name tests");
        await repo.RunAsync("git config commit.gpgsign false");
        return repo;
    }

    // A working repo wired to a fresh bare remote with one seed commit pushed and upstream tracking set,
    // so pull/push operations have a real remote to talk to.
    public static async Task<TempGitRepo> CreateWithRemoteAsync()
    {
        var repo = await CreateAsync();
        var remote = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ra-remote-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(remote);
        await RunCommand.RunAsync("git init --bare -q", new RunCommandOptions(Cwd: remote));
        repo.RemotePath = remote;

        await repo.WriteAsync("seed.txt", "seed");
        await repo.RunAsync("git add -A");
        await repo.RunAsync("git commit -q -m seed");
        await repo.RunAsync($"git remote add origin \"{remote}\"");
        await repo.RunAsync("git push -u origin HEAD -q");
        return repo;
    }

    public Task WriteAsync(string rel, string contents)
        => File.WriteAllTextAsync(System.IO.Path.Combine(Path, rel), contents);

    public Task<string> ReadAsync(string rel)
        => File.ReadAllTextAsync(System.IO.Path.Combine(Path, rel));

    public async Task CommitAllAsync(string message)
    {
        await RunAsync("git add -A");
        await RunAsync($"git commit -q -m \"{message}\"");
    }

    public async Task RunAsync(string cmd)
    {
        var r = await RunCommand.RunAsync(cmd, new RunCommandOptions(Cwd: Path));
        if (r.ExitCode != 0)
            throw new InvalidOperationException($"TempGitRepo: {cmd} -> {r.ErrorText}");
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { /* best-effort cleanup */ }
        if (RemotePath is not null)
            try { Directory.Delete(RemotePath, recursive: true); } catch { /* best-effort cleanup */ }
    }
}
