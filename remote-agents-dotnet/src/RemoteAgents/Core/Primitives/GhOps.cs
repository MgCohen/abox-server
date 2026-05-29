using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteAgents.Primitives;

public sealed record GhPrCreateRequest(
    string ProjectDir,
    string Title,
    string Body,
    // Source branch. Defaults to the current branch when null — matches
    // gh's own behavior.
    string? Head = null,
    // Target branch. Defaults to the repo's default branch when null.
    string? Base_ = null,
    bool Draft = false);

public sealed record GhPrCommentRequest(
    string ProjectDir,
    // Either a PR number or a branch name. gh accepts both as the
    // selector argument.
    string Selector,
    string Body);

public sealed record GhPrViewRequest(
    string ProjectDir,
    // Null = "the PR for the current branch", which is what Track B
    // wants when checking "did I already open one?"
    string? Selector = null);

// One row from `gh pr view --json ...`. Only the fields we look at
// today; extend as needed. State is one of "OPEN", "CLOSED", "MERGED".
public sealed record GhPrInfo(
    int Number,
    string State,
    string Title,
    string Url,
    string HeadRefName,
    string BaseRefName,
    bool IsDraft);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GhPrInfo))]
internal sealed partial class GhJsonContext : JsonSerializerContext { }

// Thin wrapper around the `gh` CLI. Same pattern as GitOps — record
// per verb, async method returns either a parsed struct (when there's
// one) or the raw RunCommandResult.
//
// Authentication is the caller's responsibility (gh auth login once on
// the box). We don't inject credentials; we don't catch auth failures
// — they surface as InvalidOperationException with gh's stderr.
public static class GhOps
{
    public static async Task<string> PrCreateAsync(GhPrCreateRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            throw new ArgumentException("gh pr create: title required", nameof(req));
        if (string.IsNullOrWhiteSpace(req.Body))
            throw new ArgumentException("gh pr create: body required", nameof(req));

        // We pass title via -F and body via stdin (-F -) so multi-line
        // bodies, quotes, and shell metacharacters can't break.
        // RunCommand exposes a single Input, so we squash title+body via
        // a sentinel: actually gh wants them as separate flags. Easiest:
        // write body to a temp file and use --body-file. Cheap.
        var bodyFile = Path.Combine(Path.GetTempPath(), $"ra-pr-body-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(bodyFile, req.Body, ct);
        try
        {
            var parts = new List<string>
            {
                "gh pr create",
                "--title", Quote(req.Title),
                "--body-file", Quote(bodyFile),
            };
            if (req.Head is not null) { parts.Add("--head"); parts.Add(Quote(req.Head)); }
            if (req.Base_ is not null) { parts.Add("--base"); parts.Add(Quote(req.Base_)); }
            if (req.Draft) parts.Add("--draft");

            var res = await RunCommand.RunAsync(string.Join(' ', parts), new RunCommandOptions(Cwd: req.ProjectDir), ct);
            if (res.ExitCode != 0)
                throw new InvalidOperationException($"gh pr create failed: {(string.IsNullOrEmpty(res.Stderr) ? res.Stdout : res.Stderr)}");

            // gh prints the PR URL on stdout on success — return it so
            // callers can log / surface it.
            return res.Stdout.Trim();
        }
        finally
        {
            try { File.Delete(bodyFile); } catch { }
        }
    }

    public static async Task<RunCommandResult> PrCommentAsync(GhPrCommentRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Body))
            throw new ArgumentException("gh pr comment: body required", nameof(req));

        var bodyFile = Path.Combine(Path.GetTempPath(), $"ra-pr-comment-{Guid.NewGuid():N}.md");
        await File.WriteAllTextAsync(bodyFile, req.Body, ct);
        try
        {
            var res = await RunCommand.RunAsync(
                $"gh pr comment {Quote(req.Selector)} --body-file {Quote(bodyFile)}",
                new RunCommandOptions(Cwd: req.ProjectDir), ct);
            if (res.ExitCode != 0)
                throw new InvalidOperationException($"gh pr comment failed: {(string.IsNullOrEmpty(res.Stderr) ? res.Stdout : res.Stderr)}");
            return res;
        }
        finally
        {
            try { File.Delete(bodyFile); } catch { }
        }
    }

    // Look up a PR. Returns null when no PR exists for the selector —
    // gh exits 1 with "no pull requests found" on its stderr. Anything
    // else throws.
    public static async Task<GhPrInfo?> PrViewAsync(GhPrViewRequest req, CancellationToken ct = default)
    {
        const string fields = "number,state,title,url,headRefName,baseRefName,isDraft";
        var sel = req.Selector is null ? "" : " " + Quote(req.Selector);
        var res = await RunCommand.RunAsync(
            $"gh pr view{sel} --json {fields}",
            new RunCommandOptions(Cwd: req.ProjectDir), ct);

        if (res.ExitCode != 0)
        {
            // gh prints "no pull requests found" to stderr when the
            // current branch (or named branch) has no PR. Treat that as
            // "not found" — not a primitive error.
            if (res.Stderr.Contains("no pull requests found", StringComparison.OrdinalIgnoreCase))
                return null;
            throw new InvalidOperationException($"gh pr view failed: {(string.IsNullOrEmpty(res.Stderr) ? res.Stdout : res.Stderr)}");
        }

        return JsonSerializer.Deserialize(res.Stdout, GhJsonContext.Default.GhPrInfo);
    }

    private static readonly char[] QuoteTriggers = { ' ', '\t', '"' };
    private static string Quote(string s)
    {
        if (s.IndexOfAny(QuoteTriggers) < 0) return s;
        return "\"" + s.Replace("\"", "\\\"") + "\"";
    }
}
