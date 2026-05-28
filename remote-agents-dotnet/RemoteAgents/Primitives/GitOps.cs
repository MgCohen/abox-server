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

// One entry from `git status --porcelain`: the 2-char status code
// (e.g. "??" untracked, " M" modified-unstaged, "M " staged) plus the
// relative path with original separators.
public sealed record GitStatusEntry(string Code, string Path);

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

    private static string NormalizeSlashes(string s) => s.Replace('\\', '/');

    private static readonly Regex SafePath = new(@"^[A-Za-z0-9_./-]+$", RegexOptions.Compiled);

    private static string Quote(string s)
    {
        if (SafePath.IsMatch(s)) return s;
        return "\"" + s.Replace("\"", "\\\"") + "\"";
    }
}
