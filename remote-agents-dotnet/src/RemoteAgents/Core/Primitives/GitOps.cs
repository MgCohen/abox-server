using System.Text.RegularExpressions;

namespace RemoteAgents.Primitives;

public sealed record GitDiffRequest(string ProjectDir, bool Staged = false, IReadOnlyList<string>? Paths = null);

public sealed record GitAddRequest(string ProjectDir, IReadOnlyList<string> Files);

public sealed record GitCommitRequest(
    string ProjectDir,
    string Message,
    IReadOnlyList<string>? Files = null,
    string? CoAuthor = null);

public sealed record GitPushRequest(
    string ProjectDir,
    string Remote = "origin",
    string? Branch = null,
    bool Force = false);

public sealed record GitFetchRequest(
    string ProjectDir,
    string Remote = "origin",
    string? Branch = null,
    bool Prune = false,
    bool Tags = false);

public sealed record GitPullRequest(
    string ProjectDir,
    string Remote = "origin",
    string? Branch = null,
    // Default ff-only: pulls that would create a merge commit fail loudly
    // instead of silently auto-merging. Set Rebase=true for rebase-pull.
    bool FfOnly = true,
    bool Rebase = false);

public sealed record GitCheckoutRequest(
    string ProjectDir,
    string Ref,
    // When true, create a new branch named Ref (optionally from StartPoint).
    bool CreateBranch = false,
    string? StartPoint = null);

public sealed record GitBranchCreateRequest(
    string ProjectDir,
    string Name,
    string? StartPoint = null,
    // Set upstream to <remote>/<Name> when non-null.
    string? TrackRemote = null);

public sealed record GitBranchDeleteRequest(
    string ProjectDir,
    string Name,
    bool Force = false);

public sealed record GitMergeRequest(
    string ProjectDir,
    string Ref,
    bool FfOnly = false,
    bool NoFf = false,
    string? Message = null);

public sealed record GitRebaseRequest(
    string ProjectDir,
    string Upstream,
    string? Onto = null);

public sealed record GitLogRequest(
    string ProjectDir,
    int MaxCount = 20,
    string? Range = null,
    // Pretty format. Default matches `--oneline` for cheap parsing.
    string Format = "%H %s");

// One entry from `git status --porcelain`: the 2-char status code
// (e.g. "??" untracked, " M" modified-unstaged, "M " staged) plus the
// relative path with original separators.
public sealed record GitStatusEntry(string Code, string Path);

// Subject + hash from `git log --pretty=...`. Range/MaxCount in the
// request shape what comes back.
public sealed record GitLogEntry(string Hash, string Subject);

public static class GitOps
{
    public static async Task<string> DiffAsync(GitDiffRequest req, CancellationToken ct = default)
    {
        var flag = req.Staged ? "--staged" : "";
        var pathArg = req.Paths is { Count: > 0 } ? " -- " + string.Join(' ', req.Paths.Select(Quote)) : "";
        var res = await RunCommand.RunAsync($"git diff {flag}{pathArg}", new RunCommandOptions(Cwd: req.ProjectDir), ct);
        return res.Stdout;
    }

    public static async Task<string> DiffStatAsync(GitDiffRequest req, CancellationToken ct = default)
    {
        var flag = req.Staged ? "--staged" : "";
        var res = await RunCommand.RunAsync($"git diff --stat {flag}", new RunCommandOptions(Cwd: req.ProjectDir), ct);
        return res.Stdout;
    }

    public static async Task<RunCommandResult> AddAsync(GitAddRequest req, CancellationToken ct = default)
    {
        if (req.Files is null || req.Files.Count == 0)
            throw new ArgumentException("GitOps.AddAsync: files list required (no implicit \"add all\")", nameof(req));

        var quoted = string.Join(' ', req.Files.Select(Quote));
        var res = await RunCommand.RunAsync($"git add {quoted}", new RunCommandOptions(Cwd: req.ProjectDir), ct);
        if (res.ExitCode != 0)
            throw new InvalidOperationException($"git add failed: {(string.IsNullOrEmpty(res.Stderr) ? res.Stdout : res.Stderr)}");
        return res;
    }

    public static async Task<RunCommandResult> CommitAsync(GitCommitRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
            throw new ArgumentException("GitOps.CommitAsync: message is required", nameof(req));

        if (req.Files is { Count: > 0 })
            await AddAsync(new GitAddRequest(req.ProjectDir, req.Files), ct);

        var fullMessage = req.CoAuthor is null
            ? req.Message.Trim()
            : $"{req.Message.Trim()}\n\nCo-Authored-By: {req.CoAuthor} <noreply@anthropic.com>";

        var res = await RunCommand.RunAsync(
            "git commit -F -",
            new RunCommandOptions(Cwd: req.ProjectDir, Input: fullMessage),
            ct);

        if (res.ExitCode != 0)
            throw new InvalidOperationException($"git commit failed: {(string.IsNullOrEmpty(res.Stderr) ? res.Stdout : res.Stderr)}");
        return res;
    }

    public static async Task<RunCommandResult> PushAsync(GitPushRequest req, CancellationToken ct = default)
    {
        if (req.Force && (req.Branch == "main" || req.Branch == "master"))
            throw new InvalidOperationException($"push: refusing to force-push to {req.Branch} via this primitive");

        var parts = new List<string> { "git push" };
        if (req.Force) parts.Add("--force-with-lease");
        parts.Add(req.Remote);
        if (req.Branch is not null) parts.Add(req.Branch);

        var res = await RunCommand.RunAsync(string.Join(' ', parts), new RunCommandOptions(Cwd: req.ProjectDir), ct);
        if (res.ExitCode != 0)
            throw new InvalidOperationException($"git push failed: {(string.IsNullOrEmpty(res.Stderr) ? res.Stdout : res.Stderr)}");
        return res;
    }

    public static async Task<string> CurrentBranchAsync(string projectDir, CancellationToken ct = default)
    {
        var res = await RunCommand.RunAsync("git rev-parse --abbrev-ref HEAD", new RunCommandOptions(Cwd: projectDir), ct);
        return res.Stdout.Trim();
    }

    public static async Task<bool> IsDirtyAsync(string projectDir, CancellationToken ct = default)
    {
        var res = await RunCommand.RunAsync("git status --porcelain", new RunCommandOptions(Cwd: projectDir), ct);
        return res.Stdout.Trim().Length > 0;
    }

    // Files that `git status --porcelain` reports as changed in the working
    // tree or index, relative to projectDir with forward-slash separators.
    // Use this — not FsDiff — as the source of truth at commit time:
    // FsDiff compares mtime, which gets bumped by `git checkout --` even
    // though git itself sees the file as clean.
    //
    // Renames are reported as the destination path (after the " -> ").
    public static async Task<IReadOnlyList<string>> ChangedFilesAsync(string projectDir, CancellationToken ct = default)
        => (await ChangedFilesWithStatusAsync(projectDir, ct)).Select(e => e.Path).ToList();

    // Same data as ChangedFilesAsync but also reports the porcelain status
    // code ("??" for untracked, " M" for modified-unstaged, etc.).
    // Useful when you need to differentiate tracked vs untracked.
    public static async Task<IReadOnlyList<GitStatusEntry>> ChangedFilesWithStatusAsync(string projectDir, CancellationToken ct = default)
    {
        var res = await RunCommand.RunAsync("git status --porcelain", new RunCommandOptions(Cwd: projectDir), ct);
        var entries = new List<GitStatusEntry>();
        foreach (var raw in res.Stdout.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length < 4) continue;
            var code = line.Substring(0, 2);
            // Porcelain v1: XY<space>path  or  XY<space>old -> new
            var rest = line.Substring(3);
            var arrow = rest.IndexOf(" -> ", StringComparison.Ordinal);
            var path = arrow >= 0 ? rest.Substring(arrow + 4) : rest;
            path = path.Trim().Trim('"');
            if (path.Length > 0) entries.Add(new GitStatusEntry(code, path));
        }
        return entries;
    }

    // Revert every unstaged change in projectDir except files whose
    // relative path appears in `protectedPaths`. Used by unity-review
    // to wipe TMP_SDF / .meta / Library noise generated by Unity batch
    // mode while keeping Claude's actual source edits.
    //
    // Paths in `protectedPaths` are compared as forward-slash relative
    // paths (same shape as ChangedFilesAsync returns).
    //
    // Behavior by porcelain status:
    //   "??"      — untracked. Deleted with `git clean -f -- <path>`.
    //   anything  — tracked-modified. Restored with `git checkout -- <path>`.
    // Returns the list of paths actually reverted/cleaned.
    public static async Task<IReadOnlyList<string>> RestoreUnstagedExceptAsync(
        string projectDir,
        IReadOnlyCollection<string> protectedPaths,
        CancellationToken ct = default)
    {
        var keep = new HashSet<string>(protectedPaths.Select(NormalizeSlashes), StringComparer.OrdinalIgnoreCase);
        var entries = await ChangedFilesWithStatusAsync(projectDir, ct);
        var reverted = new List<string>();
        foreach (var entry in entries)
        {
            var norm = NormalizeSlashes(entry.Path);
            if (keep.Contains(norm)) continue;

            string cmd = entry.Code == "??"
                ? $"git clean -f -- {Quote(entry.Path)}"
                : $"git checkout -- {Quote(entry.Path)}";

            var res = await RunCommand.RunAsync(cmd, new RunCommandOptions(Cwd: projectDir), ct);
            if (res.ExitCode == 0) reverted.Add(entry.Path);
        }
        return reverted;
    }

    public static async Task<RunCommandResult> FetchAsync(GitFetchRequest req, CancellationToken ct = default)
    {
        var parts = new List<string> { "git fetch" };
        if (req.Prune) parts.Add("--prune");
        if (req.Tags) parts.Add("--tags");
        parts.Add(req.Remote);
        if (req.Branch is not null) parts.Add(req.Branch);

        var res = await RunCommand.RunAsync(string.Join(' ', parts), new RunCommandOptions(Cwd: req.ProjectDir), ct);
        if (res.ExitCode != 0)
            throw new InvalidOperationException($"git fetch failed: {(string.IsNullOrEmpty(res.Stderr) ? res.Stdout : res.Stderr)}");
        return res;
    }

    public static async Task<RunCommandResult> PullAsync(GitPullRequest req, CancellationToken ct = default)
    {
        if (req.FfOnly && req.Rebase)
            throw new ArgumentException("git pull: --ff-only and --rebase are mutually exclusive", nameof(req));

        var parts = new List<string> { "git pull" };
        if (req.FfOnly) parts.Add("--ff-only");
        if (req.Rebase) parts.Add("--rebase");
        parts.Add(req.Remote);
        if (req.Branch is not null) parts.Add(req.Branch);

        var res = await RunCommand.RunAsync(string.Join(' ', parts), new RunCommandOptions(Cwd: req.ProjectDir), ct);
        if (res.ExitCode != 0)
            throw new InvalidOperationException($"git pull failed: {(string.IsNullOrEmpty(res.Stderr) ? res.Stdout : res.Stderr)}");
        return res;
    }

    public static async Task<RunCommandResult> CheckoutAsync(GitCheckoutRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Ref))
            throw new ArgumentException("git checkout: ref required", nameof(req));

        var parts = new List<string> { "git checkout" };
        if (req.CreateBranch) parts.Add("-b");
        parts.Add(Quote(req.Ref));
        if (req.CreateBranch && req.StartPoint is not null) parts.Add(Quote(req.StartPoint));

        var res = await RunCommand.RunAsync(string.Join(' ', parts), new RunCommandOptions(Cwd: req.ProjectDir), ct);
        if (res.ExitCode != 0)
            throw new InvalidOperationException($"git checkout failed: {(string.IsNullOrEmpty(res.Stderr) ? res.Stdout : res.Stderr)}");
        return res;
    }

    public static async Task<RunCommandResult> BranchCreateAsync(GitBranchCreateRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            throw new ArgumentException("git branch: name required", nameof(req));

        var parts = new List<string> { "git branch", Quote(req.Name) };
        if (req.StartPoint is not null) parts.Add(Quote(req.StartPoint));

        var res = await RunCommand.RunAsync(string.Join(' ', parts), new RunCommandOptions(Cwd: req.ProjectDir), ct);
        if (res.ExitCode != 0)
            throw new InvalidOperationException($"git branch create failed: {(string.IsNullOrEmpty(res.Stderr) ? res.Stdout : res.Stderr)}");

        if (req.TrackRemote is not null)
        {
            var setUp = await RunCommand.RunAsync(
                $"git branch --set-upstream-to={Quote(req.TrackRemote + "/" + req.Name)} {Quote(req.Name)}",
                new RunCommandOptions(Cwd: req.ProjectDir), ct);
            if (setUp.ExitCode != 0)
                throw new InvalidOperationException($"git branch --set-upstream-to failed: {(string.IsNullOrEmpty(setUp.Stderr) ? setUp.Stdout : setUp.Stderr)}");
        }
        return res;
    }

    public static async Task<RunCommandResult> BranchDeleteAsync(GitBranchDeleteRequest req, CancellationToken ct = default)
    {
        if (req.Name == "main" || req.Name == "master")
            throw new InvalidOperationException($"branch-delete: refusing to delete {req.Name} via this primitive");

        var flag = req.Force ? "-D" : "-d";
        var res = await RunCommand.RunAsync($"git branch {flag} {Quote(req.Name)}", new RunCommandOptions(Cwd: req.ProjectDir), ct);
        if (res.ExitCode != 0)
            throw new InvalidOperationException($"git branch delete failed: {(string.IsNullOrEmpty(res.Stderr) ? res.Stdout : res.Stderr)}");
        return res;
    }

    // Local branches, in `git branch` order. The current branch is not
    // marked with "*" — call CurrentBranchAsync for that.
    public static async Task<IReadOnlyList<string>> BranchListAsync(string projectDir, CancellationToken ct = default)
    {
        var res = await RunCommand.RunAsync("git branch --list", new RunCommandOptions(Cwd: projectDir), ct);
        if (res.ExitCode != 0)
            throw new InvalidOperationException($"git branch --list failed: {(string.IsNullOrEmpty(res.Stderr) ? res.Stdout : res.Stderr)}");

        var branches = new List<string>();
        foreach (var raw in res.Stdout.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("* ")) line = line[2..].Trim();
            // Detached HEAD marker: "(HEAD detached at <ref>)". Skip.
            if (line.StartsWith('(')) continue;
            branches.Add(line);
        }
        return branches;
    }

    public static async Task<RunCommandResult> MergeAsync(GitMergeRequest req, CancellationToken ct = default)
    {
        if (req.FfOnly && req.NoFf)
            throw new ArgumentException("git merge: --ff-only and --no-ff are mutually exclusive", nameof(req));
        if (string.IsNullOrWhiteSpace(req.Ref))
            throw new ArgumentException("git merge: ref required", nameof(req));

        var parts = new List<string> { "git merge" };
        if (req.FfOnly) parts.Add("--ff-only");
        if (req.NoFf) parts.Add("--no-ff");
        if (req.Message is not null) { parts.Add("-m"); parts.Add(Quote(req.Message)); }
        parts.Add(Quote(req.Ref));

        var res = await RunCommand.RunAsync(string.Join(' ', parts), new RunCommandOptions(Cwd: req.ProjectDir), ct);
        if (res.ExitCode != 0)
            throw new InvalidOperationException($"git merge failed: {(string.IsNullOrEmpty(res.Stderr) ? res.Stdout : res.Stderr)}");
        return res;
    }

    public static async Task<RunCommandResult> RebaseAsync(GitRebaseRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Upstream))
            throw new ArgumentException("git rebase: upstream required", nameof(req));

        var parts = new List<string> { "git rebase" };
        if (req.Onto is not null) { parts.Add("--onto"); parts.Add(Quote(req.Onto)); }
        parts.Add(Quote(req.Upstream));

        var res = await RunCommand.RunAsync(string.Join(' ', parts), new RunCommandOptions(Cwd: req.ProjectDir), ct);
        if (res.ExitCode != 0)
            throw new InvalidOperationException($"git rebase failed: {(string.IsNullOrEmpty(res.Stderr) ? res.Stdout : res.Stderr)}");
        return res;
    }

    public static async Task<IReadOnlyList<GitLogEntry>> LogAsync(GitLogRequest req, CancellationToken ct = default)
    {
        // Pin separator to git's `%x09` (literal tab) so the format
        // string has no whitespace at the cmd.exe level — a real tab
        // would get parsed as an argument boundary before git ever sees
        // it. We rewrite the documented `%H %s` shape to `%H%x09%s`.
        var format = string.IsNullOrEmpty(req.Format) ? "%H%x09%s" : req.Format.Replace(" ", "%x09");
        var parts = new List<string> { "git log", $"--max-count={req.MaxCount}", $"--pretty=format:{format}" };
        if (req.Range is not null) parts.Add(Quote(req.Range));

        var res = await RunCommand.RunAsync(string.Join(' ', parts), new RunCommandOptions(Cwd: req.ProjectDir), ct);
        if (res.ExitCode != 0)
            throw new InvalidOperationException($"git log failed: {(string.IsNullOrEmpty(res.Stderr) ? res.Stdout : res.Stderr)}");

        var entries = new List<GitLogEntry>();
        foreach (var raw in res.Stdout.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            var tab = line.IndexOf('\t');
            if (tab < 0) { entries.Add(new GitLogEntry(line, "")); continue; }
            entries.Add(new GitLogEntry(line[..tab], line[(tab + 1)..]));
        }
        return entries;
    }

    private static string NormalizeSlashes(string s) => s.Replace('\\', '/');

    private static readonly Regex SafePath = new(@"^[A-Za-z0-9_./-]+$", RegexOptions.Compiled);

    private static string Quote(string s)
    {
        if (SafePath.IsMatch(s)) return s;
        return "\"" + s.Replace("\"", "\\\"") + "\"";
    }
}
