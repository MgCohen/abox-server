using RemoteAgents.Flows;
using RemoteAgents.Tools.CommandLine;

namespace RemoteAgents.Actors.Git;

public sealed class Git
{
    public IOperation<DirtyResult> CheckDirty() =>
        new GitOperation<DirtyResult>("git-dirty", (ctx, ct) => CheckDirtyAsync(ctx.ProjectDir, ct));

    public IOperation<DiffResult> Diff() =>
        new GitOperation<DiffResult>("git-diff", (ctx, ct) => DiffAsync(ctx.ProjectDir, ct));

    public IOperation<ChangedFilesResult> ChangedFiles() =>
        new GitOperation<ChangedFilesResult>("git-changed-files", (ctx, ct) => ChangedFilesAsync(ctx.ProjectDir, ct));

    public IOperation<GitCommitResult> Commit(string message, IReadOnlyList<string> files, string? coAuthor = null) =>
        new GitOperation<GitCommitResult>("git-commit", (ctx, ct) => CommitAsync(ctx.ProjectDir, message, files, coAuthor, ct));

    public IOperation<GitPushResult> Push(string remote = "origin", string? branch = null, bool force = false) =>
        new GitOperation<GitPushResult>("git-push", (ctx, ct) => PushAsync(ctx.ProjectDir, remote, branch, force, ct));

    private static async Task<DirtyResult> CheckDirtyAsync(string projectDir, CancellationToken ct)
    {
        var res = (await RunCommand.RunAsync("git status --porcelain", new RunCommandOptions(Cwd: projectDir), ct))
            .EnsureOk("git status");
        return new DirtyResult(res.Stdout.Trim().Length > 0);
    }

    private static async Task<DiffResult> DiffAsync(string projectDir, CancellationToken ct)
    {
        var res = (await RunCommand.RunAsync("git diff", new RunCommandOptions(Cwd: projectDir), ct))
            .EnsureOk("git diff");
        return new DiffResult(res.Stdout, CountFiles(res.Stdout));
    }

    private static async Task<ChangedFilesResult> ChangedFilesAsync(string projectDir, CancellationToken ct)
    {
        var res = (await RunCommand.RunAsync("git status --porcelain", new RunCommandOptions(Cwd: projectDir), ct))
            .EnsureOk("git status");
        return new ChangedFilesResult(ParsePorcelainPaths(res.Stdout));
    }

    private static async Task<GitCommitResult> CommitAsync(
        string projectDir, string message, IReadOnlyList<string> files, string? coAuthor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("git commit: message is required", nameof(message));
        if (files.Count == 0)
            throw new ArgumentException("git commit: an explicit file list is required (no implicit add -A)", nameof(files));

        var quoted = string.Join(' ', files.Select(Shell.QuoteArg));
        (await RunCommand.RunAsync($"git add {quoted}", new RunCommandOptions(Cwd: projectDir), ct)).EnsureOk("git add");

        var fullMessage = coAuthor is null
            ? message.Trim()
            : $"{message.Trim()}\n\nCo-Authored-By: {coAuthor} <noreply@anthropic.com>";
        (await RunCommand.RunAsync("git commit -F -", new RunCommandOptions(Cwd: projectDir, Input: fullMessage), ct))
            .EnsureOk("git commit");

        var head = (await RunCommand.RunAsync("git rev-parse HEAD", new RunCommandOptions(Cwd: projectDir), ct))
            .EnsureOk("git rev-parse");
        return new GitCommitResult(head.Stdout.Trim(), FirstLine(message));
    }

    private static async Task<GitPushResult> PushAsync(
        string projectDir, string remote, string? branch, bool force, CancellationToken ct)
    {
        if (force && IsProtected(branch))
            throw new InvalidOperationException($"git push: refusing to force-push to {branch}");

        var target = branch ?? await CurrentBranchAsync(projectDir, ct);
        if (force && IsProtected(target))
            throw new InvalidOperationException($"git push: refusing to force-push to {target}");

        var parts = new List<string> { "git push" };
        if (force) parts.Add("--force-with-lease");
        parts.Add(remote);
        parts.Add(target);
        (await RunCommand.RunAsync(string.Join(' ', parts), new RunCommandOptions(Cwd: projectDir), ct)).EnsureOk("git push");
        return new GitPushResult(remote, target);
    }

    private static async Task<string> CurrentBranchAsync(string projectDir, CancellationToken ct)
    {
        var res = (await RunCommand.RunAsync("git rev-parse --abbrev-ref HEAD", new RunCommandOptions(Cwd: projectDir), ct))
            .EnsureOk("git rev-parse");
        return res.Stdout.Trim();
    }

    private static bool IsProtected(string? branch) => branch is "main" or "master";

    private static IReadOnlyList<string> ParsePorcelainPaths(string stdout)
    {
        var paths = new List<string>();
        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length < 4) continue;
            var rest = line[3..];
            var arrow = rest.IndexOf(" -> ", StringComparison.Ordinal);
            var path = (arrow >= 0 ? rest[(arrow + 4)..] : rest).Trim().Trim('"');
            if (path.Length > 0) paths.Add(path);
        }
        return paths;
    }

    private static int CountFiles(string diff)
    {
        var count = 0;
        foreach (var line in diff.Split('\n'))
            if (line.StartsWith("diff --git ", StringComparison.Ordinal)) count++;
        return count;
    }

    private static string FirstLine(string message)
    {
        var nl = message.IndexOf('\n');
        return (nl >= 0 ? message[..nl] : message).Trim();
    }

    private sealed class GitOperation<T>(string name, Func<FlowContext, CancellationToken, Task<T>> run)
        : IOperation<T>
    {
        public string Name => name;

        public Task<T> Execute(FlowContext ctx, CancellationToken ct) => run(ctx, ct);
    }
}
