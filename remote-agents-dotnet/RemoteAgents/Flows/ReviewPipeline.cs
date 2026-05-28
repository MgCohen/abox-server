using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using RemoteAgents.Agents;
using RemoteAgents.Events;
using RemoteAgents.Primitives;
using RemoteAgents.Sessions;
using RemoteAgents.Validation;

namespace RemoteAgents.Flows;

[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CodexReviewArtifact))]
internal sealed partial class FlowsJsonContext : JsonSerializerContext { }

public sealed record CodexReviewArtifact(string Verdict, string SessionId, string Text);

public sealed record ReviewPipelineOptions(
    string FlowName,
    int MaxFixAttempts = 3,
    int MaxRevisionRounds = 1,
    // When true: snapshot Claude-touched files before validating, then
    // restore everything else after. Used by Unity-style validators that
    // dirty the tree on every run (TMP_SDF, .meta, Library/, etc.).
    bool IsolateClaudeChanges = false,
    string ValidationLabel = "all project checks passed",
    string ReviewProjectKind = "changes",
    string CoAuthor = "Claude Opus 4.7 + Codex gpt-5.5");

public sealed record ReviewPipelineResult(string Outcome, int ExitCode)
{
    public bool Shipped => Outcome == "shipped";
}

// Shared Claude→validate→Codex→revise→commit pipeline. full-review.cs and
// unity-review.cs differ only in the validator instance and a couple of
// labels — those become parameters on ReviewPipelineOptions.
public static class ReviewPipeline
{
    public static async Task<ReviewPipelineResult> RunAsync(
        Session session,
        IEventSink sink,
        string projectDir,
        string userPrompt,
        bool shouldPush,
        IValidator validator,
        ReviewPipelineOptions opts,
        CancellationToken ct = default)
    {
        var claude = new ClaudeAgent { Name = "claude", Sink = sink };

        // 1. Claude does the work
        var claudeResult = await claude.RunAsync(new AgentRunRequest(userPrompt, null, projectDir), ct);
        Console.WriteLine($"[claude] turn 1 done (session={claudeResult.SessionId})\n");

        // Optional: capture what Claude actually changed BEFORE the
        // validator gets a chance to spray noise across the tree.
        IReadOnlyList<string>? claudeTouched = opts.IsolateClaudeChanges
            ? await GitOps.ChangedFilesAsync(projectDir, ct)
            : null;

        // 2. validate + fix loop
        var (validationOk, _) = await RunValidateFixLoopAsync(
            claude, claudeResult, validator, projectDir,
            opts.MaxFixAttempts, opts.FlowName == "unity-review", ct);

        if (!validationOk)
        {
            session.End("validation-failed");
            return new("validation-failed", 2);
        }

        if (claudeTouched is not null)
        {
            var reverted = await GitOps.RestoreUnstagedExceptAsync(projectDir, claudeTouched, ct);
            if (reverted.Count > 0)
                Console.WriteLine($"[cleanup] reverted {reverted.Count} validator-generated files.");
        }

        // 3. Codex review
        var diffText = await GitOps.DiffAsync(new GitDiffRequest(projectDir), ct);
        if (string.IsNullOrWhiteSpace(diffText))
        {
            Console.WriteLine("[done] Claude made no file changes. Nothing to review or commit.");
            session.End("no-changes");
            return new("no-changes", 0);
        }

        var reviewPrompt = BuildReviewPrompt(userPrompt, diffText, opts.ReviewProjectKind, opts.ValidationLabel);
        Console.WriteLine($"[codex] reviewing diff ({diffText.Length} bytes)...");

        var codex = new CodexAgent
        {
            Name = "codex",
            Sink = sink,
            Options = new CodexAgentOptions(Sandbox: "read-only", JsonStreamTimeoutMs: 5 * 60_000),
        };
        var review = await codex.RunAsync(new AgentRunRequest(reviewPrompt, null, projectDir), ct);
        var verdict = ParseVerdict(review.Text);

        Console.WriteLine("[codex] review:");
        foreach (var line in review.Text.Trim().Split('\n')) Console.WriteLine("  " + line);
        Console.WriteLine();

        await WriteReviewArtifactAsync(session.Dir, verdict, review.SessionId, review.Text, ct);

        if (verdict == "unclear")
        {
            Console.Error.WriteLine($"[abort] Codex verdict unclear (review was {review.Text.Length} bytes). Refusing to commit.");
            session.End("verdict-unclear");
            return new("verdict-unclear", 2);
        }

        // 4. one revision round
        if (verdict == "revise" && opts.MaxRevisionRounds > 0)
        {
            Console.WriteLine("[revise] sending reviewer feedback to Claude...");
            claudeResult = await claude.RunAsync(new AgentRunRequest(
                $"Code reviewer feedback — please address:\n\n{review.Text}",
                claudeResult.SessionId, projectDir), ct);

            var v2 = await validator.ValidateAsync(projectDir, ct);
            if (!v2.Ok)
            {
                Console.Error.WriteLine($"[abort] post-revision validation failed: {v2.Summary}");
                session.End("revision-broke-validation");
                return new("revision-broke-validation", 2);
            }
        }

        // 5. commit (+ optional push)
        var filesToCommit = await GitOps.ChangedFilesAsync(projectDir, ct);
        if (filesToCommit.Count == 0)
        {
            Console.WriteLine("[done] No files ultimately changed.");
            session.End("no-changes");
            return new("no-changes", 0);
        }

        var commitMessage = BuildCommitMessage(userPrompt, review.Text);
        Console.WriteLine($"[commit] {filesToCommit.Count} files...");
        await GitOps.CommitAsync(new GitCommitRequest(
            ProjectDir: projectDir,
            Message: commitMessage,
            Files: filesToCommit,
            CoAuthor: opts.CoAuthor), ct);
        Console.WriteLine("[commit] done.");

        if (shouldPush)
        {
            var branch = await GitOps.CurrentBranchAsync(projectDir, ct);
            Console.WriteLine($"[push] origin {branch}...");
            await GitOps.PushAsync(new GitPushRequest(projectDir, Branch: branch), ct);
            Console.WriteLine("[push] done.");
        }

        await File.WriteAllTextAsync(Path.Combine(session.Dir, "claude-raw.txt"), claudeResult.RawOutput, ct);
        await File.WriteAllTextAsync(Path.Combine(session.Dir, "codex-review.txt"), review.Text, ct);

        session.End("shipped");
        Console.WriteLine();
        Console.WriteLine("──────────────────────────────────────────");
        Console.WriteLine($"Shipped. Transcript: {session.Dir}");
        return new("shipped", 0);
    }

    private static async Task<(bool ok, ValidationResult last)> RunValidateFixLoopAsync(
        ClaudeAgent claude,
        AgentResult initialResult,
        IValidator validator,
        string projectDir,
        int maxAttempts,
        bool isUnityValidator,
        CancellationToken ct)
    {
        var last = new ValidationResult(false, "", "");
        var claudeResult = initialResult;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var suffix = isUnityValidator ? " (Unity batch-mode, this can take minutes)" : "";
            Console.WriteLine($"[validate] attempt {attempt}{suffix}...");
            last = await validator.ValidateAsync(projectDir, ct);
            if (last.Ok)
            {
                Console.WriteLine($"[validate] PASSED — {last.Summary}\n");
                return (true, last);
            }
            Console.WriteLine($"[validate] FAILED — {last.Summary}");
            if (attempt == maxAttempts) break;

            var fixPrompt =
                $"Validation{(isUnityValidator ? " (Unity batch-mode compile)" : "")} failed. " +
                $"Address these issues:\n\n{last.Errors}";
            claudeResult = await claude.RunAsync(new AgentRunRequest(fixPrompt, claudeResult.SessionId, projectDir), ct);
            Console.WriteLine($"[claude] fix turn {attempt + 1} done\n");
        }
        Console.Error.WriteLine($"[abort] validation never passed after {maxAttempts} attempts.");
        return (false, last);
    }

    public static string BuildReviewPrompt(string userPrompt, string diffText, string projectKind, string validationLabel)
        => string.Join("\n", new[]
        {
            $"You are reviewing {projectKind} made by another agent.",
            "",
            "Original task:",
            userPrompt,
            "",
            "Diff:",
            "```diff",
            diffText,
            "```",
            "",
            $"Validation: {validationLabel}.",
            "",
            "Reply with EXACTLY one of:",
            "  APPROVE: <one-sentence reason>  — if the work is acceptable to ship.",
            "  REVISE: <issues>                — if it needs another pass.",
            "",
            "Be strict but not pedantic. Don't ask for cosmetic changes.",
        });

    public static string ParseVerdict(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith("APPROVE:", StringComparison.OrdinalIgnoreCase)) return "approve";
        if (trimmed.StartsWith("REVISE:",  StringComparison.OrdinalIgnoreCase)) return "revise";
        return "unclear";
    }

    public static string BuildCommitMessage(string userPrompt, string reviewText)
    {
        var firstLine = reviewText.Split('\n')[0];
        var reviewLine = VerdictPrefix.Replace(firstLine, "").Trim();
        return string.Join("\n", new[]
        {
            Truncate(userPrompt, 70),
            "",
            userPrompt,
            "",
            $"Reviewed by Codex: {(string.IsNullOrEmpty(reviewLine) ? "(no comment)" : reviewLine)}",
        });
    }

    public static async Task WriteReviewArtifactAsync(string sessionDir, string verdict, string sessionId, string text, CancellationToken ct = default)
    {
        var artifact = new CodexReviewArtifact(verdict, sessionId, text);
        var json = JsonSerializer.Serialize(artifact, FlowsJsonContext.Default.CodexReviewArtifact);
        await File.WriteAllTextAsync(Path.Combine(sessionDir, "codex-review.jsonl"), json + "\n", ct);
    }

    private static readonly Regex VerdictPrefix = new(
        "^(APPROVE|REVISE):\\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}
