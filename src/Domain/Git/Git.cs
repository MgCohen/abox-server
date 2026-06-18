using ABox.Infrastructure.CommandLine;
using ABox.Infrastructure.Operations;

namespace ABox.Domain.Git;

public sealed class Git(string projectDir)
{
    public Operation<StatusArgs, StatusResult> Status { get; } = new StatusOp(projectDir);
    public Operation<DiffArgs, DiffResult> Diff { get; } = new DiffOp(projectDir);
    public Operation<CommitArgs, CommitResult> Commit { get; } = new CommitOp(projectDir);
    public Operation<PushArgs, PushResult> Push { get; } = new PushOp(projectDir);
    public Operation<PullArgs, PullResult> Pull { get; } = new PullOp(projectDir);

    private sealed class StatusOp(string dir) : Operation<StatusArgs, StatusResult>
    {
        protected override async Task<StatusResult> Invoke(StatusArgs args, CancellationToken ct)
        {
            var res = (await RunCommand.RunAsync("git status --porcelain", new RunCommandOptions(Cwd: dir), ct))
                .EnsureOk("git status");
            return new StatusResult(res.Stdout.Trim().Length > 0, ParsePorcelainPaths(res.Stdout));
        }
    }

    private sealed class DiffOp(string dir) : Operation<DiffArgs, DiffResult>
    {
        protected override async Task<DiffResult> Invoke(DiffArgs args, CancellationToken ct)
        {
            var res = (await RunCommand.RunAsync("git diff", new RunCommandOptions(Cwd: dir), ct))
                .EnsureOk("git diff");
            return new DiffResult(res.Stdout, CountFiles(res.Stdout));
        }
    }

    private sealed class CommitOp(string dir) : Operation<CommitArgs, CommitResult>
    {
        protected override async Task<CommitResult> Invoke(CommitArgs args, CancellationToken ct)
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
            return new CommitResult(head.Stdout.Trim(), FirstLine(args.Message));
        }
    }

    private sealed class PushOp(string dir) : Operation<PushArgs, PushResult>
    {
        protected override async Task<PushResult> Invoke(PushArgs args, CancellationToken ct)
        {
            if (args.Force && IsProtected(args.Branch))
                throw new InvalidOperationException($"git push: refusing to force-push to {args.Branch}");

            var target = args.Branch ?? await CurrentBranchAsync(dir, ct);
            if (args.Force && IsProtected(target))
                throw new InvalidOperationException($"git push: refusing to force-push to {target}");

            var parts = new List<string> { "git push" };
            // --force-if-includes pairs with the lease: a lease alone is defeated by a background fetch that
            // advances the tracking ref without integrating it (spike research/stacked-prs.md §9).
            if (args.Force) { parts.Add("--force-with-lease"); parts.Add("--force-if-includes"); }
            parts.Add(args.Remote);
            parts.Add(target);
            (await RunCommand.RunAsync(string.Join(' ', parts), new RunCommandOptions(Cwd: dir), ct)).EnsureOk("git push");
            return new PushResult(args.Remote, target);
        }
    }

    private sealed class PullOp(string dir) : Operation<PullArgs, PullResult>
    {
        protected override async Task<PullResult> Invoke(PullArgs args, CancellationToken ct)
        {
            var parts = new List<string> { "git pull" };
            if (args.Rebase) parts.Add("--rebase");
            parts.Add(args.Remote);
            if (args.Branch is not null) parts.Add(args.Branch);
            var res = (await RunCommand.RunAsync(string.Join(' ', parts), new RunCommandOptions(Cwd: dir), ct))
                .EnsureOk("git pull");
            var updated = !res.Stdout.Contains("Already up to date", StringComparison.OrdinalIgnoreCase);
            return new PullResult(args.Remote, args.Branch ?? "(current)", updated);
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
