using System.Text.RegularExpressions;
using RemoteAgents.Agents;
using RemoteAgents.Primitives;

namespace RemoteAgents.Flows;

// Reviewer's verdict. Provider-agnostic (D9): any IAgent can play the
// reviewer role; this record just carries the parsed result.
public sealed record ReviewVerdict(Verdict Verdict, string SessionId, string Text);

// Build the review prompt, run any IAgent against the project's diff, parse
// the APPROVE/REVISE verdict. Pure orchestration over the agent contract —
// no provider-specific knowledge, no artifact persistence.
public static class Reviews
{
    private static readonly Regex VerdictPrefix = new(
        "^(APPROVE|REVISE):\\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<ReviewVerdict> AskAgentForVerdictAsync(
        IAgent reviewer,
        string projectDir,
        string userPrompt,
        string projectKind,
        string validationLabel,
        CancellationToken ct = default)
    {
        var diffText = await GitOps.DiffAsync(new GitDiffRequest(projectDir), ct);
        var reviewPrompt = BuildReviewPrompt(userPrompt, diffText, projectKind, validationLabel);
        var review = await reviewer.RunAsync(new AgentRunRequest(reviewPrompt, null, projectDir), ct);
        return new ReviewVerdict(ParseVerdict(review.Text), review.SessionId, review.Text);
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

    public static Verdict ParseVerdict(string text)
    {
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith("APPROVE:", StringComparison.OrdinalIgnoreCase)) return Verdict.Approve;
        if (trimmed.StartsWith("REVISE:",  StringComparison.OrdinalIgnoreCase)) return Verdict.Revise;
        return Verdict.Unclear;
    }

    public static string BuildCommitMessage(string userPrompt, string reviewText, string coAuthor)
    {
        var firstLine = reviewText.Split('\n')[0];
        var reviewLine = VerdictPrefix.Replace(firstLine, "").Trim();
        return string.Join("\n", new[]
        {
            Truncate(userPrompt, 70),
            "",
            userPrompt,
            "",
            $"Reviewed by: {(string.IsNullOrEmpty(reviewLine) ? "(no comment)" : reviewLine)}",
            "",
            $"Co-Authored-By: {coAuthor}",
        });
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}
