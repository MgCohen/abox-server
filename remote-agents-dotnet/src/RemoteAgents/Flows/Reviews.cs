using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using RemoteAgents.Agents;
using RemoteAgents.Events;
using RemoteAgents.Primitives;

namespace RemoteAgents.Flows;

[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CodexReviewArtifact))]
internal sealed partial class FlowsJsonContext : JsonSerializerContext { }

public sealed record CodexReviewArtifact(Verdict Verdict, string SessionId, string Text);

public sealed record CodexVerdict(Verdict Verdict, string Text, string SessionId)
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

    public static async Task<CodexVerdict> AskCodexForVerdictAsync(
        string projectDir,
        string sessionDir,
        string userPrompt,
        string projectKind,
        string validationLabel,
        IEventSink sink,
        CodexAgentOptions? codexOptions = null,
        CancellationToken ct = default)
    {
        var diffText = await GitOps.DiffAsync(new GitDiffRequest(projectDir), ct);
        var reviewPrompt = BuildReviewPrompt(userPrompt, diffText, projectKind, validationLabel);

        await sink.PhaseStartAsync("codex", $"reviewing diff ({diffText.Length} bytes)...", ct);

        var codex = new CodexAgent
        {
            Name = "codex",
            Sink = sink,
            Options = codexOptions ?? new CodexAgentOptions(Sandbox: "read-only", JsonStreamTimeoutMs: 5 * 60_000),
        };
        var review = await codex.RunAsync(new AgentRunRequest(reviewPrompt, null, projectDir), ct);
        var verdict = ParseVerdict(review.Text);

        await sink.PhaseInfoAsync("codex", FormatReviewBlock(review.Text), ct);
        await WriteReviewArtifactAsync(sessionDir, verdict, review.SessionId, review.Text, ct);

        return new CodexVerdict(verdict, review.Text, review.SessionId);
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

    public static async Task WriteReviewArtifactAsync(string sessionDir, Verdict verdict, string sessionId, string text, CancellationToken ct = default)
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

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}
