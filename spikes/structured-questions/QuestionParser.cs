using System.Text.Json;

namespace StructuredQuestions;

public readonly record struct ParseDiagnostics(
    bool SentinelFound,
    bool JsonExtracted,
    bool Parsed,
    bool Degraded,
    AgentQuestion? Question);

public static class QuestionParser
{
    public const string Sentinel = "<<NEEDS_INPUT>>";

    public static AgentQuestion? TryParse(string finalText) => Diagnose(finalText).Question;

    public static ParseDiagnostics Diagnose(string finalText)
    {
        if (string.IsNullOrEmpty(finalText))
            return new ParseDiagnostics(false, false, false, false, null);

        var idx = finalText.LastIndexOf(Sentinel, StringComparison.Ordinal);
        if (idx < 0)
            return new ParseDiagnostics(false, false, false, false, null);

        var tail = finalText[(idx + Sentinel.Length)..].Trim();
        var json = ExtractFirstJsonObject(tail);
        if (json is null)
            return Degrade(tail, jsonExtracted: false);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var kind = GetPropertyOrNull(root, "kind")?.GetString();
            var prompt = GetPropertyOrNull(root, "prompt")?.GetString();
            if (string.IsNullOrWhiteSpace(prompt))
                return Degrade(tail, jsonExtracted: true);

            if (kind == "choice"
                && root.TryGetProperty("options", out var opts)
                && opts.ValueKind == JsonValueKind.Array)
            {
                var options = opts.EnumerateArray()
                    .Select(o => o.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!)
                    .ToList();
                if (options.Count > 0)
                {
                    var choice = new AgentQuestion.Choice(
                        prompt!, options,
                        AllowFreeText: GetPropertyOrNull(root, "allow_free_text")?.GetBoolean() ?? false,
                        RawTail: tail);
                    return new ParseDiagnostics(true, true, true, false, choice);
                }
            }

            var open = new AgentQuestion.Open(prompt!, RawTail: tail);
            return new ParseDiagnostics(true, true, true, false, open);
        }
        catch (JsonException)
        {
            return Degrade(tail, jsonExtracted: true);
        }
    }

    private static ParseDiagnostics Degrade(string tail, bool jsonExtracted)
        => new(true, jsonExtracted, false, true, new AgentQuestion.Open(tail, tail));

    private static JsonElement? GetPropertyOrNull(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v)
            ? v
            : null;

    // First balanced {...}, tracking string state so braces inside JSON strings
    // don't unbalance the scan. Tolerates a ```json fence, a leading sentence,
    // or trailing prose after the object.
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
