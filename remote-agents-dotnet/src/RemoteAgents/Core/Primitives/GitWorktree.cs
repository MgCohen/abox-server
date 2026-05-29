namespace RemoteAgents.Primitives;

public sealed record GitWorktreeAddRequest(
    // The repo whose worktrees we're managing (usually the primary
    // checkout). Doesn't need to be the new worktree's path.
    string RepoDir,
    // Where the new worktree will live on disk.
    string Path,
    // Branch to check out in the new worktree. Required — we never use
    // `git worktree add <path>` with implicit branch derivation; it has
    // surprising defaults.
    string Branch,
    // When true, create the branch as part of `git worktree add` (-b).
    bool CreateBranch = false,
    // Start point for the new branch when CreateBranch is true.
    string? StartPoint = null,
    // Reuse an existing branch already checked out elsewhere. Dangerous;
    // off by default.
    bool Force = false);

public sealed record GitWorktreeRemoveRequest(
    string RepoDir,
    string Path,
    // Force removal even if the worktree is dirty.
    bool Force = false);

// One row from `git worktree list --porcelain`. Branch is null for
// detached HEADs; Locked is non-null when `locked` is present (the empty
// string when the lock has no reason).
public sealed record GitWorktreeEntry(
    string Path,
    string Head,
    string? Branch,
    string? Locked,
    bool Prunable);

// `git worktree` wrappers. Track B uses these to spin up per-task
// worktrees, drive Claude inside them, and tear them down once a PR is
// pushed.
public static class GitWorktree
{
    public static async Task<RunCommandResult> AddAsync(GitWorktreeAddRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Path))
            throw new ArgumentException("worktree add: path required", nameof(req));
        if (string.IsNullOrWhiteSpace(req.Branch))
            throw new ArgumentException("worktree add: branch required", nameof(req));

        var parts = new List<string> { "git worktree add" };
        if (req.Force) parts.Add("--force");
        if (req.CreateBranch) { parts.Add("-b"); parts.Add(QuoteIdent(req.Branch)); parts.Add(QuotePath(req.Path)); if (req.StartPoint is not null) parts.Add(QuoteIdent(req.StartPoint)); }
        else { parts.Add(QuotePath(req.Path)); parts.Add(QuoteIdent(req.Branch)); }

        var res = await RunCommand.RunAsync(string.Join(' ', parts), new RunCommandOptions(Cwd: req.RepoDir), ct);
        if (res.ExitCode != 0)
            throw new InvalidOperationException($"git worktree add failed: {(string.IsNullOrEmpty(res.Stderr) ? res.Stdout : res.Stderr)}");
        return res;
    }

    public static async Task<RunCommandResult> RemoveAsync(GitWorktreeRemoveRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Path))
            throw new ArgumentException("worktree remove: path required", nameof(req));

        var parts = new List<string> { "git worktree remove" };
        if (req.Force) parts.Add("--force");
        parts.Add(QuotePath(req.Path));

        var res = await RunCommand.RunAsync(string.Join(' ', parts), new RunCommandOptions(Cwd: req.RepoDir), ct);
        if (res.ExitCode != 0)
            throw new InvalidOperationException($"git worktree remove failed: {(string.IsNullOrEmpty(res.Stderr) ? res.Stdout : res.Stderr)}");
        return res;
    }

    // Parse `git worktree list --porcelain`. Records are separated by
    // blank lines; each record starts with a `worktree <path>` line.
    public static async Task<IReadOnlyList<GitWorktreeEntry>> ListAsync(string repoDir, CancellationToken ct = default)
    {
        var res = await RunCommand.RunAsync("git worktree list --porcelain", new RunCommandOptions(Cwd: repoDir), ct);
        if (res.ExitCode != 0)
            throw new InvalidOperationException($"git worktree list failed: {(string.IsNullOrEmpty(res.Stderr) ? res.Stdout : res.Stderr)}");

        var entries = new List<GitWorktreeEntry>();
        string? path = null, head = null, branch = null, locked = null;
        bool prunable = false, detached = false;

        void Flush()
        {
            if (path is null) return;
            entries.Add(new GitWorktreeEntry(
                Path: path,
                Head: head ?? "",
                Branch: detached ? null : branch,
                Locked: locked,
                Prunable: prunable));
            path = head = branch = locked = null;
            prunable = detached = false;
        }

        foreach (var raw in res.Stdout.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) { Flush(); continue; }

            var sp = line.IndexOf(' ');
            var key = sp < 0 ? line : line[..sp];
            var val = sp < 0 ? "" : line[(sp + 1)..];

            switch (key)
            {
                case "worktree": path = val; break;
                case "HEAD": head = val; break;
                case "branch":
                    // `refs/heads/<name>` → `<name>`
                    branch = val.StartsWith("refs/heads/") ? val["refs/heads/".Length..] : val;
                    break;
                case "detached": detached = true; break;
                case "locked": locked = val; break; // val may be empty
                case "prunable": prunable = true; break;
            }
        }
        Flush();
        return entries;
    }

    public static async Task<RunCommandResult> PruneAsync(string repoDir, CancellationToken ct = default)
    {
        var res = await RunCommand.RunAsync("git worktree prune", new RunCommandOptions(Cwd: repoDir), ct);
        if (res.ExitCode != 0)
            throw new InvalidOperationException($"git worktree prune failed: {(string.IsNullOrEmpty(res.Stderr) ? res.Stdout : res.Stderr)}");
        return res;
    }

    // Path quoting: same shape as GitOps.Quote, copied to keep the two
    // primitives independent. Branch names share the rule.
    private static string QuotePath(string s) => Needs(s) ? "\"" + s.Replace("\"", "\\\"") + "\"" : s;
    private static string QuoteIdent(string s) => Needs(s) ? "\"" + s.Replace("\"", "\\\"") + "\"" : s;
    private static bool Needs(string s) => s.IndexOfAny(QuoteTriggers) >= 0;
    private static readonly char[] QuoteTriggers = { ' ', '\t', '"' };
}
