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

    private static readonly Regex SafePath = new(@"^[A-Za-z0-9_./-]+$", RegexOptions.Compiled);

    private static string Quote(string s)
    {
        if (SafePath.IsMatch(s)) return s;
        return "\"" + s.Replace("\"", "\\\"") + "\"";
    }
}
