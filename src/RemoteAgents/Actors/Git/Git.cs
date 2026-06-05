using RemoteAgents.Flows;
using RemoteAgents.Tools.CommandLine;

namespace RemoteAgents.Actors.Git;

public sealed class Git(string projectDir)
{
    public Flow.Operation<DirtyArgs, DirtyResult> CheckDirty { get; } = new DirtyOp(projectDir);
    public Flow.Operation<DiffArgs, DiffResult> Diff { get; } = new DiffOp(projectDir);
    public Flow.Operation<ChangedFilesArgs, ChangedFilesResult> ChangedFiles { get; } = new ChangedFilesOp(projectDir);
    public Flow.Operation<CommitArgs, GitCommitResult> Commit { get; } = new CommitOp(projectDir);
    public Flow.Operation<PushArgs, GitPushResult> Push { get; } = new PushOp(projectDir);

    private sealed class DirtyOp(string dir) : Flow.Operation<DirtyArgs, DirtyResult>
    {
        protected override async Task<DirtyResult> Invoke(DirtyArgs args, CancellationToken ct)
        {
            var res = (await RunCommand.RunAsync("git status --porcelain", new RunCommandOptions(Cwd: dir), ct))
                .EnsureOk("git status");
            return new DirtyResult(res.Stdout.Trim().Length > 0);
        }
    }

    private sealed class DiffOp(string dir) : Flow.Operation<DiffArgs, DiffResult>
    {
        protected override async Task<DiffResult> Invoke(DiffArgs args, CancellationToken ct)
        {
            var res = (await RunCommand.RunAsync("git diff", new RunCommandOptions(Cwd: dir), ct))
                .EnsureOk("git diff");
            return new DiffResult(res.Stdout, CountFiles(res.Stdout));
        }
    }

    private sealed class ChangedFilesOp(string dir) : Flow.Operation<ChangedFilesArgs, ChangedFilesResult>
    {
        protected override async Task<ChangedFilesResult> Invoke(ChangedFilesArgs args, CancellationToken ct)
        {
            var res = (await RunCommand.RunAsync("git status --porcelain", new RunCommandOptions(Cwd: dir), ct))
                .EnsureOk("git status");
            return new ChangedFilesResult(ParsePorcelainPaths(res.Stdout));
        }
    }

    private sealed class CommitOp(string dir) : Flow.Operation<CommitArgs, GitCommitResult>
    {
        protected override async Task<GitCommitResult> Invoke(CommitArgs args, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(args.Message))
                throw new ArgumentException("git commit: message is required", nameof(args));
            if (args.Files.Count == 0)
                throw new ArgumentException("git commit: an explicit file list is required (no implicit add -A)", nameof(args));

            var quoted = string.Join(' ', args.Files.Select(Shell.QuoteArg));
            (await RunCommand.RunAsync($"git add {quoted}", new RunCommandOptions(Cwd: dir), ct)).EnsureOk("git add");

            var fullMessage = args.CoAuthor is null
                ? args.Message.Trim()
                : $"{args.Message.Trim()}\n\nCo-Authored-By: {args.CoAuthor} <noreply@anthropic.com>";
            (await RunCommand.RunAsync("git commit -F -", new RunCommandOptions(Cwd: dir, Input: fullMessage), ct))
                .EnsureOk("git commit");

            var head = (await RunCommand.RunAsync("git rev-parse HEAD", new RunCommandOptions(Cwd: dir), ct))
                .EnsureOk("git rev-parse");
            return new GitCommitResult(head.Stdout.Trim(), FirstLine(args.Message));
        }
    }

    private sealed class PushOp(string dir) : Flow.Operation<PushArgs, GitPushResult>
    {
        protected override async Task<GitPushResult> Invoke(PushArgs args, CancellationToken ct)
        {
            if (args.Force && IsProtected(args.Branch))
                throw new InvalidOperationException($"git push: refusing to force-push to {args.Branch}");

            var target = args.Branch ?? await CurrentBranchAsync(dir, ct);
            if (args.Force && IsProtected(target))
                throw new InvalidOperationException($"git push: refusing to force-push to {target}");

            var parts = new List<string> { "git push" };
            if (args.Force) parts.Add("--force-with-lease");
            parts.Add(args.Remote);
            parts.Add(target);
            (await RunCommand.RunAsync(string.Join(' ', parts), new RunCommandOptions(Cwd: dir), ct)).EnsureOk("git push");
            return new GitPushResult(args.Remote, target);
        }
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
}
