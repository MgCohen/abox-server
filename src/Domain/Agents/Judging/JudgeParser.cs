using System.Text.Json;

namespace ABox.Domain.Agents.Judging;

public static class JudgeParser
{
    public const string Sentinel = "<<JUDGE_VERDICT>>";

    public static IReadOnlyList<CriterionResult> Parse(string finalText, IReadOnlyList<Criterion> criteria)
    {
        var parsed = ExtractResults(finalText);
        return criteria
            .Select(c => parsed.TryGetValue(c.Id, out var r)
                ? r
                : new CriterionResult(c.Id, Verdict.Indeterminate, "no verdict returned for this criterion"))
            .ToList();
    }

    private static Dictionary<string, CriterionResult> ExtractResults(string finalText)
    {
        var empty = new Dictionary<string, CriterionResult>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(finalText)) return empty;

        var idx = finalText.LastIndexOf(Sentinel, StringComparison.Ordinal);
        if (idx < 0) return empty;

        var tail = finalText[(idx + Sentinel.Length)..].Trim();
        var json = ExtractFirstJsonObject(tail);
        if (json is null) return empty;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return empty;

            var map = new Dictionary<string, CriterionResult>(StringComparer.Ordinal);
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var id = PropOrNull(item, "criterionId")?.GetString();
                if (string.IsNullOrWhiteSpace(id)) continue;
                var status = ParseStatus(PropOrNull(item, "status")?.GetString());
                var evidence = PropOrNull(item, "evidence")?.GetString() ?? string.Empty;
                map[id!] = new CriterionResult(id!, status, evidence);
            }

            return map;
        }
        catch (JsonException)
        {
            return empty;
        }
    }

    private static Verdict ParseStatus(string? status) => status switch
    {
        "pass" => Verdict.Pass,
        "fail" => Verdict.Fail,
        _ => Verdict.Indeterminate,
    };

    private static JsonElement? PropOrNull(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) ? v : null;

    private static string? ExtractFirstJsonObject(string s)
    {
        var start = s.IndexOf('{');
        if (start < 0) return null;

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < s.Length; i++)
        {
            var c = s[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0) return s[start..(i + 1)];
                    break;
            }
        }

        return null;
    }
}
