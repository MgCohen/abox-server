using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using RemoteAgents.Agents;
using RemoteAgents.Events;
using RemoteAgents.Primitives;
using RemoteAgents.Sessions;

namespace RemoteAgents.Flows;

[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CodexVerdict))]
internal sealed partial class FlowsJsonContext : JsonSerializerContext { }

// Field order matches the historical codex-review.jsonl shape
// (Verdict, SessionId, Text) so the on-disk artifact stays byte-stable.
public sealed record CodexVerdict(Verdict Verdict, string SessionId, string Text)
{
    public bool IsApprove => Verdict == Flows.Verdict.Approve;
    public bool IsRevise  => Verdict == Flows.Verdict.Revise;
    public bool IsUnclear => Verdict == Flows.Verdict.Unclear;
}

// Build the review prompt, run Codex against the project's diff, parse
// the APPROVE/REVISE verdict, and write the codex-review.jsonl artifact
// to the session dir. Returns the parsed verdict; the caller decides
// what to do with it.
//
// projectKind: "changes" / "a Unity C# change" — parenthetical descriptor
// for the prompt's opening line.
// validationLabel: "all project checks passed" / "Unity batch-mode compile
// passed" — single line in the review prompt telling the reviewer what
// kind of validation gate has already been cleared.
public static class Reviews
{
    private static readonly Regex VerdictPrefix = new(
        "^(APPROVE|REVISE):\\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Default options for the codex reviewer. Read-only sandbox so the
    // reviewer can't accidentally edit the tree it's reviewing; 5-minute
    // JSON stream timeout because review prompts are long. Callers can
    // pass their own pre-built reviewer to override.
    public static CodexAgentOptions DefaultReviewerOptions { get; } =
        new(Sandbox: "read-only", JsonStreamTimeoutMs: 5 * 60_000);

    public static async Task<CodexVerdict> AskCodexForVerdictAsync(
        CodexAgent reviewer,
        string projectDir,
        string sessionDir,
        string userPrompt,
        string projectKind,
        string validationLabel,
        CancellationToken ct = default)
    {
        var diffText = await GitOps.DiffAsync(new GitDiffRequest(projectDir), ct);
        var reviewPrompt = BuildReviewPrompt(userPrompt, diffText, projectKind, validationLabel);

        await reviewer.Sink.PhaseStartAsync("codex", $"reviewing diff ({diffText.Length} bytes)...", ct);

        var review = await reviewer.RunAsync(new AgentRunRequest(reviewPrompt, null, projectDir), ct);
        var verdict = ParseVerdict(review.Text);

        var result = new CodexVerdict(verdict, review.SessionId, review.Text);
        await reviewer.Sink.PhaseInfoAsync("codex", FormatReviewBlock(review.Text), ct);
        await WriteReviewArtifactAsync(sessionDir, result, ct);

        return result;
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

    public static async Task WriteReviewArtifactAsync(string sessionDir, CodexVerdict verdict, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(verdict, FlowsJsonContext.Default.CodexVerdict);
        await File.WriteAllTextAsync(
            Session.GetArtifactPath(sessionDir, SessionArtifact.CodexReviewJl),
            json + "\n", ct);
    }

    private static string FormatReviewBlock(string text)
    {
        var lines = text.Trim().Split('\n');
        return "review:\n" + string.Join("\n", lines.Select(l => "  " + l));
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}
