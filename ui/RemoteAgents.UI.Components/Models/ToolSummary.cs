using System.Text.Json;

namespace RemoteAgents.UI.Components.Models;

// Per-tool extraction of a one-line summary from the ToolUse input JSON.
//
// The Conversation pane shows tool invocations as scrolling log rows
// (overstory-style) rather than collapsed cards. Each row needs an
// inline preview of what the tool is doing — the file path being read,
// the bash command being run, the glob pattern, etc. Tool-specific
// because different tools have different "canonical arg" semantics.
//
// Pure functions on input JSON. Easy to unit-test, no Blazor dependency.
public static class ToolSummary
{
    private const int MaxSummaryChars = 120;

    public static string For(string toolName, string inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson)) return "";
        JsonElement root;
        try { root = JsonDocument.Parse(inputJson).RootElement; }
        catch { return Truncate(inputJson); }

        var summary = toolName switch
        {
            "Glob"               => Pick(root, "pattern"),
            "Grep"               => Pick(root, "pattern") + PathSuffix(root),
            "Bash"               => Pick(root, "command"),
            "PowerShell"         => Pick(root, "command"),
            "Read"               => Pick(root, "file_path") + LineSuffix(root),
            "Edit"               => Pick(root, "file_path"),
            "Write"              => Pick(root, "file_path"),
            "NotebookEdit"       => Pick(root, "notebook_path"),
            "WebFetch"           => Pick(root, "url"),
            "WebSearch"          => Pick(root, "query"),
            "TodoWrite"          => TodoProgress(root),
            "Task"               => Pick(root, "description"),
            "Agent"              => Pick(root, "description"),
            "Skill"              => Pick(root, "skill"),
            _                    => FirstStringField(root),
        };

        if (string.IsNullOrEmpty(summary)) summary = Truncate(inputJson);
        return Truncate(summary);
    }

    private static string Pick(JsonElement root, string field) =>
        root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty(field, out var v)
            && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? ""
                : "";

    private static string PathSuffix(JsonElement root)
    {
        var p = Pick(root, "path");
        return string.IsNullOrEmpty(p) ? "" : $"  in {p}";
    }

    private static string LineSuffix(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return "";
        var off = root.TryGetProperty("offset", out var o) && o.ValueKind == JsonValueKind.Number
            ? o.GetInt32() : (int?)null;
        var lim = root.TryGetProperty("limit", out var l) && l.ValueKind == JsonValueKind.Number
            ? l.GetInt32() : (int?)null;
        return off is null && lim is null
            ? ""
            : $"  ({off ?? 1}..{(off ?? 1) + (lim ?? 0)})";
    }

    private static string TodoProgress(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("todos", out var arr)
            || arr.ValueKind != JsonValueKind.Array) return "todo update";
        int total = 0, done = 0;
        foreach (var t in arr.EnumerateArray())
        {
            total++;
            if (t.ValueKind == JsonValueKind.Object
                && t.TryGetProperty("status", out var s)
                && s.ValueKind == JsonValueKind.String
                && s.GetString() == "completed") done++;
        }
        return $"{done}/{total} complete";
    }

    private static string FirstStringField(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return "";
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
                return prop.Value.GetString() ?? "";
        }
        return "";
    }

    private static string Truncate(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.ReplaceLineEndings(" ").Trim();
        return s.Length <= MaxSummaryChars ? s : s[..(MaxSummaryChars - 1)] + "…";
    }

    // For tool result rows: a short status string. Truthy result content
    // shown verbatim if it's one short line; otherwise just the size.
    public static string ResultSummary(string content, bool isError)
    {
        if (isError) return "✗ error";
        if (string.IsNullOrEmpty(content)) return "(empty)";
        var firstLine = content.AsSpan();
        var nl = firstLine.IndexOfAny('\r', '\n');
        if (nl >= 0) firstLine = firstLine[..nl];
        var oneLine = firstLine.ToString().Trim();
        if (oneLine.Length > 0 && oneLine.Length <= 60 && content.Length <= 100)
            return oneLine;
        return $"{content.Length} chars";
    }
}
