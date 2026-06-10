using System.Text.Json;

namespace RemoteAgents.Actors.Agents;

public static class QuestionParser
{
    public const string Sentinel = "<<NEEDS_INPUT>>";

    public static AgentQuestion? TryParse(string finalText)
    {
        if (string.IsNullOrEmpty(finalText)) return null;

        var idx = finalText.LastIndexOf(Sentinel, StringComparison.Ordinal);
        if (idx < 0) return null;

        var tail = finalText[(idx + Sentinel.Length)..].Trim();
        var json = ExtractFirstJsonObject(tail);
        if (json is null) return new AgentQuestion.Open(tail, tail);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var kind = PropOrNull(root, "kind")?.GetString();
            var prompt = PropOrNull(root, "prompt")?.GetString();
            if (string.IsNullOrWhiteSpace(prompt)) return new AgentQuestion.Open(tail, tail);

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
                    return new AgentQuestion.Choice(
                        prompt!, options,
                        PropOrNull(root, "allow_free_text")?.GetBoolean() ?? false,
                        tail);
            }

            return new AgentQuestion.Open(prompt!, tail);
        }
        catch (JsonException)
        {
            return new AgentQuestion.Open(tail, tail);
        }
    }

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
                // A brace inside a JSON string literal must not move the depth.
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
