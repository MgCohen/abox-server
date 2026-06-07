using System.Text;

namespace StructuredQuestions;

internal static class Report
{
    internal static string PartA(List<RunRecord> records, int n)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Structured Questions Spike — Part A (emission reliability)");
        sb.AppendLine();
        sb.AppendLine($"N requested per cell: {n}");
        sb.AppendLine();
        sb.AppendLine("| provider | prompt id | runs | asked | parsed | kind ok | false-pos | freeform/hang | degraded | errors | avg ms | cost usd |");
        sb.AppendLine("|----------|-----------|------|-------|--------|---------|-----------|---------------|----------|--------|--------|----------|");

        foreach (var group in records.GroupBy(r => (r.Provider, r.PromptId)).OrderBy(g => g.Key.Provider).ThenBy(g => g.Key.PromptId))
        {
            var rows = group.ToList();
            var ok = rows.Where(r => r.Error is null).ToList();
            var asked = ok.Count(r => r.Asked);
            var parsed = ok.Count(r => r.Asked && r.Parsed);
            var kindOk = ok.Count(r => r.KindCorrect);
            var falsePos = ok.Count(r => r.FalsePositive);
            var hang = ok.Count(r => r.FreeformOrHang);
            var degraded = ok.Count(r => r.Degraded);
            var errors = rows.Count(r => r.Error is not null);
            var avgMs = ok.Count > 0 ? (long)ok.Average(r => r.DurationMs) : 0;
            var cost = ok.Where(r => r.CostUsd is not null).Sum(r => r.CostUsd!.Value);
            sb.AppendLine($"| {group.Key.Provider} | {group.Key.PromptId} | {rows.Count} | {asked} | {parsed} | {kindOk} | {falsePos} | {hang} | {degraded} | {errors} | {avgMs} | {cost:0.0000} |");
        }

        sb.AppendLine();
        AppendRates(sb, records);
        AppendFailures(sb, records);
        return sb.ToString();
    }

    private static void AppendRates(StringBuilder sb, List<RunRecord> records)
    {
        sb.AppendLine("## Rates (NeedsInput prompts only, excluding driver errors)");
        sb.AppendLine();
        foreach (var provider in records.Select(r => r.Provider).Distinct())
        {
            var needsInput = records.Where(r => r.Provider == provider && r.Error is null
                && r.ExpectedStatus.Equals("NeedsInput", StringComparison.OrdinalIgnoreCase)).ToList();
            var negatives = records.Where(r => r.Provider == provider && r.Error is null
                && !r.ExpectedStatus.Equals("NeedsInput", StringComparison.OrdinalIgnoreCase)).ToList();

            var askedCount = needsInput.Count(r => r.Asked);
            var parseRate = askedCount > 0 ? 100.0 * needsInput.Count(r => r.Asked && r.Parsed) / askedCount : 0;
            var falsePos = negatives.Count > 0 ? 100.0 * negatives.Count(r => r.FalsePositive) / negatives.Count : 0;
            var hangRate = needsInput.Count > 0 ? 100.0 * needsInput.Count(r => r.FreeformOrHang) / needsInput.Count : 0;

            sb.AppendLine($"- **{provider}** — parse rate {parseRate:0.#}% (target ≥95) · false-positive {falsePos:0.#}% (target ≈0) · hang/freeform {hangRate:0.#}%");
        }
        sb.AppendLine();
    }

    private static void AppendFailures(StringBuilder sb, List<RunRecord> records)
    {
        var misses = records.Where(r => r.Error is not null || !r.StatusCorrect || !r.KindCorrect).ToList();
        if (misses.Count == 0) return;
        sb.AppendLine("## Misses & errors (inspect raw captures)");
        sb.AppendLine();
        foreach (var m in misses)
        {
            var why = m.Error is not null ? $"ERROR: {m.Error}"
                : !m.StatusCorrect ? (m.FalsePositive ? "false-positive (asked when it should not)" : "did not ask (freeform/hang)")
                : $"kind mismatch (expected {m.ExpectedKind}, got {m.ParsedKind})";
            sb.AppendLine($"- `{m.Provider}/{m.PromptId}#{m.Iteration}` — {why}");
            if (m.Asked && m.RawTail.Length > 0)
                sb.AppendLine($"  - tail: `{OneLine(m.RawTail)}`");
        }
        sb.AppendLine();
    }

    internal static string PartB(List<ContinuityRecord> records)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Structured Questions Spike — Part B (session continuity)");
        sb.AppendLine();
        sb.AppendLine("| provider | prompt id | turn-1 kind | turn-1 question | answer given | turn-2 did NOT re-ask |");
        sb.AppendLine("|----------|-----------|-------------|-----------------|--------------|-----------------------|");
        foreach (var r in records)
            sb.AppendLine($"| {r.Provider} | {r.PromptId} | {r.Turn1Kind ?? "-"} | {OneLine(r.Turn1Question)} | {OneLine(r.AnswerGiven)} | {(r.KeptContextHeuristic ? "yes" : "no")} |");

        sb.AppendLine();
        sb.AppendLine("> `turn-2 did NOT re-ask` is a heuristic (no sentinel in turn 2). Continuity still needs an eyeball: read the turn-1/turn-2 texts below to confirm turn 2 used the answer in context rather than restarting cold.");
        sb.AppendLine();

        foreach (var r in records)
        {
            sb.AppendLine($"## {r.Provider} / {r.PromptId}");
            sb.AppendLine();
            sb.AppendLine("**Turn 1:**");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(Trim(r.Turn1Text));
            sb.AppendLine("```");
            sb.AppendLine();
            sb.AppendLine($"**Answer:** {r.AnswerGiven}");
            sb.AppendLine();
            sb.AppendLine("**Turn 2:**");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(Trim(r.Turn2Text));
            sb.AppendLine("```");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string OneLine(string s) => s.ReplaceLineEndings(" ").Trim() is { Length: > 120 } t ? t[..120] + "…" : s.ReplaceLineEndings(" ").Trim();

    private static string Trim(string s) => s.Length <= 1500 ? s : s[..1500] + "\n... (truncated)";
}
