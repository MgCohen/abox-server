using System.Text;
using System.Text.Json;

namespace RemoteAgents.Actors.Agents.Claude;

public static class ClaudeJsonl
{
    private static string ProjectsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    // Resolve by sessionId (a unique GUID we own) rather than by encoding the
    // cwd: Claude's projects/ folder encoding collapses more than the '\/:' that
    // oracle A6 documents (e.g. '.'), so a computed path is unreliable.
    public static string? ResolveSessionFile(string sessionId)
    {
        if (!Directory.Exists(ProjectsRoot)) return null;
        try
        {
            return Directory.EnumerateFiles(ProjectsRoot, sessionId + ".jsonl", SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    public static string? TryReadLastAssistantText(string sessionId, string? promptHint = null)
    {
        var lines = TryLoadLines(sessionId);
        if (lines is null) return null;

        var anchor = FindUserAnchor(lines, promptHint);
        var sb = new StringBuilder();
        for (var i = Math.Max(0, anchor + 1); i < lines.Count; i++)
        {
            if (!TryParseEntry(lines[i], out var role, out var blocks)) continue;
            if (role != "assistant") continue;
            foreach (var b in blocks)
            {
                if (b.Type != "text") continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(b.Body);
            }
        }
        return sb.ToString();
    }

    public static AgentTurn[]? TryReadLastTurnTranscript(string sessionId, string? promptHint = null)
    {
        var lines = TryLoadLines(sessionId);
        if (lines is null) return null;

        var anchor = FindUserAnchor(lines, promptHint);
        var turns = new List<AgentTurn>();
        for (var i = Math.Max(0, anchor + 1); i < lines.Count; i++)
        {
            if (!TryParseEntry(lines[i], out var role, out var blocks)) continue;
            foreach (var b in blocks)
            {
                var kind = (role, b.Type) switch
                {
                    ("assistant", "text") => AgentTurnKind.Text,
                    ("assistant", "thinking") => AgentTurnKind.Thinking,
                    ("assistant", "tool_use") => AgentTurnKind.ToolUse,
                    (_, "tool_result") => AgentTurnKind.ToolResult,
                    _ => (AgentTurnKind?)null,
                };
                if (kind is null) continue;
                turns.Add(new AgentTurn(kind.Value, b.Body));
            }
        }
        return [.. turns];
    }

    private static List<string>? TryLoadLines(string sessionId)
    {
        var path = ResolveSessionFile(sessionId);
        if (path is null) return null;
        // Re-read (not seek): Windows FileStream caches EOF on a growing file (A6).
        try { return [.. File.ReadAllLines(path)]; }
        catch { return null; }
    }

    private static int FindUserAnchor(List<string> lines, string? promptHint)
    {
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (!TryParseEntry(lines[i], out var role, out var blocks)) continue;
            if (role != "user") continue;
            var joined = string.Concat(blocks.Where(b => b.Type == "text").Select(b => b.Body));
            if (joined.Length == 0) continue;
            if (promptHint is null || joined.Contains(promptHint, StringComparison.Ordinal)) return i;
        }
        return -1;
    }

    private readonly record struct Block(string Type, string Body);

    private static bool TryParseEntry(string line, out string role, out List<Block> blocks)
    {
        role = "";
        blocks = [];

        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{') return false;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch { return false; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String)
                role = t.GetString() ?? "";

            if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object)
                return true;

            if (msg.TryGetProperty("role", out var r) && r.ValueKind == JsonValueKind.String)
                role = r.GetString() ?? role;

            if (!msg.TryGetProperty("content", out var content)) return true;

            if (content.ValueKind == JsonValueKind.String)
            {
                blocks.Add(new Block("text", content.GetString() ?? ""));
                return true;
            }
            if (content.ValueKind != JsonValueKind.Array) return true;

            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object) continue;
                var bt = "";
                if (block.TryGetProperty("type", out var btv) && btv.ValueKind == JsonValueKind.String)
                    bt = btv.GetString() ?? "";
                blocks.Add(new Block(bt, ExtractBody(bt, block)));
            }
            return true;
        }
    }

    private static string ExtractBody(string blockType, JsonElement block)
    {
        switch (blockType)
        {
            case "text":
                return block.TryGetProperty("text", out var tx) && tx.ValueKind == JsonValueKind.String
                    ? tx.GetString() ?? ""
                    : "";

            case "thinking":
                if (block.TryGetProperty("thinking", out var th) && th.ValueKind == JsonValueKind.String)
                    return th.GetString() ?? "";
                if (block.TryGetProperty("text", out var thText) && thText.ValueKind == JsonValueKind.String)
                    return thText.GetString() ?? "";
                return "";

            case "tool_use":
            {
                var name = block.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString() ?? "" : "";
                var input = block.TryGetProperty("input", out var inp) ? inp.GetRawText() : "{}";
                return $"{{\"name\":{JsonSerializer.Serialize(name)},\"input\":{input}}}";
            }

            case "tool_result":
            {
                if (!block.TryGetProperty("content", out var rc)) return "";
                if (rc.ValueKind == JsonValueKind.String) return rc.GetString() ?? "";
                if (rc.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder();
                    foreach (var part in rc.EnumerateArray())
                    {
                        if (part.ValueKind != JsonValueKind.Object) continue;
                        if (part.TryGetProperty("text", out var pt) && pt.ValueKind == JsonValueKind.String)
                        {
                            if (sb.Length > 0) sb.Append('\n');
                            sb.Append(pt.GetString());
                        }
                    }
                    return sb.ToString();
                }
                return rc.GetRawText();
            }

            default:
                return block.GetRawText();
        }
    }
}
