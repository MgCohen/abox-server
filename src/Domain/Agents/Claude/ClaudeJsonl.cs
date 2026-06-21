using System.Text;
using System.Text.Json;
using ABox.Infrastructure.Json;

namespace ABox.Domain.Agents.Claude;

public static class ClaudeJsonl
{
    public static string DefaultProjectsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    // Resolve by sessionId (a unique GUID we own) rather than by encoding the
    // cwd: Claude's projects/ folder encoding collapses more than the '\/:' that
    // oracle A6 documents (e.g. '.'), so a computed path is unreliable. The root is
    // the box's mounted HOME/.claude/projects in a real turn (ADR 0013).
    public static string? ResolveSessionFile(string sessionId, string? projectsRoot = null)
    {
        var root = projectsRoot ?? DefaultProjectsRoot;
        if (!Directory.Exists(root)) return null;
        try
        {
            return Directory.EnumerateFiles(root, sessionId + ".jsonl", SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    public static string? TryReadLastAssistantText(string sessionId, string? promptHint = null, string? projectsRoot = null)
    {
        var lines = TryLoadLines(sessionId, projectsRoot);
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

    public static AgentTurn[]? TryReadLastTurnTranscript(string sessionId, string? promptHint = null, string? projectsRoot = null)
    {
        var lines = TryLoadLines(sessionId, projectsRoot);
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

    private static List<string>? TryLoadLines(string sessionId, string? projectsRoot)
    {
        var path = ResolveSessionFile(sessionId, projectsRoot);
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

        if (!JsonLine.TryParseObject(line, out var doc)) return false;

        using (doc)
        {
            var root = doc.RootElement;
            role = JsonLine.StringProp(root, "type") ?? "";

            if (JsonLine.ObjProp(root, "message") is not { } msg) return true;

            role = JsonLine.StringProp(msg, "role") ?? role;

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
                var bt = JsonLine.StringProp(block, "type") ?? "";
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
                return JsonLine.StringProp(block, "text") ?? "";

            case "thinking":
                return JsonLine.StringProp(block, "thinking") ?? JsonLine.StringProp(block, "text") ?? "";

            case "tool_use":
            {
                var name = JsonLine.StringProp(block, "name") ?? "";
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
                        if (JsonLine.StringProp(part, "text") is not { } text) continue;
                        if (sb.Length > 0) sb.Append('\n');
                        sb.Append(text);
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
