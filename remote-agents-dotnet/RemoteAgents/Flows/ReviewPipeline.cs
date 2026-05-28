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
    // Suffix appended to "[validate] attempt N" — set to e.g.
    // " (Unity batch-mode, this can take minutes)" for slow validators.
    string ValidationProgressNote = "",
    // Parenthetical descriptor in the fix prompt sent to Claude.
    // Empty by default → "Validation failed."; set to
    // "Unity batch-mode compile" → "Validation (Unity batch-mode compile) failed."
    string FixPromptValidationDescriptor = "",
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
//
// The pipeline emits AgentEvent.Phase for every step; ConsoleSink renders
// them on stdout/stderr. No Console.WriteLine here — control flow only.
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
        await sink.PhaseOkAsync("claude", $"turn 1 done (session={claudeResult.SessionId})", ct);

        // Optional: capture what Claude actually changed BEFORE the
        // validator gets a chance to spray noise across the tree.
        IReadOnlyList<string>? claudeTouched = opts.IsolateClaudeChanges
            ? await GitOps.ChangedFilesAsync(projectDir, ct)
            : null;

        // 2. validate + fix loop
        var (validationOk, _) = await RunValidateFixLoopAsync(
            sink, claude, claudeResult, validator, projectDir, opts, ct);

        if (!validationOk)
        {
            session.End("validation-failed");
            return new("validation-failed", 2);
        }

        if (claudeTouched is not null)
        {
            var reverted = await GitOps.RestoreUnstagedExceptAsync(projectDir, claudeTouched, ct);
            if (reverted.Count > 0)
                await sink.PhaseInfoAsync("cleanup", $"reverted {reverted.Count} validator-generated files.", ct);
        }

        // 3. Codex review
        var diffText = await GitOps.DiffAsync(new GitDiffRequest(projectDir), ct);
        if (string.IsNullOrWhiteSpace(diffText))
        {
            await sink.PhaseInfoAsync("done", "Claude made no file changes. Nothing to review or commit.", ct);
            session.End("no-changes");
            return new("no-changes", 0);
        }

        var reviewPrompt = BuildReviewPrompt(userPrompt, diffText, opts.ReviewProjectKind, opts.ValidationLabel);
        await sink.PhaseStartAsync("codex", $"reviewing diff ({diffText.Length} bytes)...", ct);

        var codex = new CodexAgent
        {
            Name = "codex",
            Sink = sink,
            Options = new CodexAgentOptions(Sandbox: "read-only", JsonStreamTimeoutMs: 5 * 60_000),
        };
        var review = await codex.RunAsync(new AgentRunRequest(reviewPrompt, null, projectDir), ct);
        var verdict = ParseVerdict(review.Text);

        await sink.PhaseInfoAsync("codex", FormatReviewBlock(review.Text), ct);
        await WriteReviewArtifactAsync(session.Dir, verdict, review.SessionId, review.Text, ct);

        if (verdict == "unclear")
        {
            await sink.PhaseFailAsync("abort",
                $"Codex verdict unclear (review was {review.Text.Length} bytes). Refusing to commit.", ct);
            session.End("verdict-unclear");
            return new("verdict-unclear", 2);
        }

        // 4. one revision round
        if (verdict == "revise" && opts.MaxRevisionRounds > 0)
        {
            await sink.PhaseStartAsync("revise", "sending reviewer feedback to Claude...", ct);
            claudeResult = await claude.RunAsync(new AgentRunRequest(
                $"Code reviewer feedback — please address:\n\n{review.Text}",
                claudeResult.SessionId, projectDir), ct);

            var v2 = await validator.ValidateAsync(projectDir, ct);
            if (!v2.Ok)
            {
                await sink.PhaseFailAsync("abort", $"post-revision validation failed: {v2.Summary}", ct);
                session.End("revision-broke-validation");
                return new("revision-broke-validation", 2);
            }
        }

        // 5. commit (+ optional push)
        var filesToCommit = await GitOps.ChangedFilesAsync(projectDir, ct);
        if (filesToCommit.Count == 0)
        {
            await sink.PhaseInfoAsync("done", "No files ultimately changed.", ct);
            session.End("no-changes");
            return new("no-changes", 0);
        }

        var commitMessage = BuildCommitMessage(userPrompt, review.Text);
        await sink.PhaseStartAsync("commit", $"{filesToCommit.Count} files...", ct);
        await GitOps.CommitAsync(new GitCommitRequest(
            ProjectDir: projectDir,
            Message: commitMessage,
            Files: filesToCommit,
            CoAuthor: opts.CoAuthor), ct);
        await sink.PhaseOkAsync("commit", "done.", ct);

        if (shouldPush)
        {
            var branch = await GitOps.CurrentBranchAsync(projectDir, ct);
            await sink.PhaseStartAsync("push", $"origin {branch}...", ct);
            await GitOps.PushAsync(new GitPushRequest(projectDir, Branch: branch), ct);
            await sink.PhaseOkAsync("push", "done.", ct);
        }

        await File.WriteAllTextAsync(Path.Combine(session.Dir, "claude-raw.txt"), claudeResult.RawOutput, ct);
        await File.WriteAllTextAsync(Path.Combine(session.Dir, "codex-review.txt"), review.Text, ct);

        session.End("shipped");
        await sink.PhaseOkAsync("done", $"Shipped. Transcript: {session.Dir}", ct);
        return new("shipped", 0);
    }

    private static async Task<(bool ok, ValidationResult last)> RunValidateFixLoopAsync(
        IEventSink sink,
        ClaudeAgent claude,
        AgentResult initialResult,
        IValidator validator,
        string projectDir,
        ReviewPipelineOptions opts,
        CancellationToken ct)
    {
        var last = new ValidationResult(false, "", "");
        var claudeResult = initialResult;
        for (var attempt = 1; attempt <= opts.MaxFixAttempts; attempt++)
        {
            await sink.PhaseStartAsync("validate", $"attempt {attempt}{opts.ValidationProgressNote}...", ct);
            last = await validator.ValidateAsync(projectDir, ct);
            if (last.Ok)
            {
                await sink.PhaseOkAsync("validate", $"PASSED — {last.Summary}", ct);
                return (true, last);
            }
            await sink.PhaseFailAsync("validate", $"FAILED — {last.Summary}", ct);
            if (attempt == opts.MaxFixAttempts) break;

            var descriptor = string.IsNullOrEmpty(opts.FixPromptValidationDescriptor)
                ? ""
                : $" ({opts.FixPromptValidationDescriptor})";
            var fixPrompt = $"Validation{descriptor} failed. Address these issues:\n\n{last.Errors}";
            claudeResult = await claude.RunAsync(new AgentRunRequest(fixPrompt, claudeResult.SessionId, projectDir), ct);
            await sink.PhaseOkAsync("claude", $"fix turn {attempt + 1} done", ct);
        }
        await sink.PhaseFailAsync("abort", $"validation never passed after {opts.MaxFixAttempts} attempts.", ct);
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

    private static string FormatReviewBlock(string text)
    {
        var lines = text.Trim().Split('\n');
        return "review:\n" + string.Join("\n", lines.Select(l => "  " + l));
    }

    private static readonly Regex VerdictPrefix = new(
        "^(APPROVE|REVISE):\\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}
