using System.Text;
using System.Text.Json;
using RemoteAgents.Agents;

namespace RemoteAgents.Providers.Claude;

// Reads the per-session JSONL Claude Code writes to
// `~/.claude/projects/<encoded-cwd>/<sessionId>.jsonl`.
//
// Two readers:
//   * TryReadLastAssistantText — just the assistant prose (final answer).
//     Backstop for AgentResult.Text when the PTY buffer is unreliable.
//   * TryReadLastTurnTranscript — full ordered turn list: every text /
//     thinking / tool_use / tool_result block after the matching user
//     prompt. Used to populate AgentResult.Transcript so the UI can show
//     "what the agent actually did", not just the final answer.
//
// Both anchor on the most recent user-text block matching promptHint (or
// the most recent user-text block as fallback).
public static class ClaudeJsonl
{
    // Resolve the path Claude Code writes to for (projectDir, sessionId).
    // Returns the path even if the file doesn't exist yet — caller can
    // probe with File.Exists.
    public static string PathFor(string projectDir, string sessionId)
    {
        // Claude Code's on-disk encoding for its `projects/` folder names —
        // backslash, forward slash, and colon all collapse to a dash.
        // Example: C:\Unity\CardFramework → C--Unity-CardFramework.
        var encoded = projectDir.Replace('\\', '-').Replace('/', '-').Replace(':', '-');
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "projects", encoded, sessionId + ".jsonl");
    }

    // Extract the text Claude emitted on the last turn — defined as every
    // assistant `text` content block that appears after the most recent
    // user prompt-text. If `promptHint` is non-null, prefer the user
    // message whose text contains promptHint; otherwise take the last
    // user message with a "text" block.
    //
    // Returns null if the file is missing or unparseable. Returns "" if
    // the file exists but had no assistant text (e.g. tool-use only).
    public static string? TryReadLastAssistantText(string projectDir, string sessionId, string? promptHint = null)
    {
        var lines = TryLoadLines(projectDir, sessionId);
        if (lines is null) return null;

        var anchor = FindUserAnchor(lines, promptHint);

        var sb = new StringBuilder();
        for (int i = Math.Max(0, anchor + 1); i < lines.Count; i++)
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

    // Extract the full ordered transcript for the last turn — every text,
    // thinking, tool_use, tool_result block from any role after the
    // matching user anchor.
    //
    // tool_use bodies are the raw JSON-encoded { name, input } so downstream
    // can show the tool call exactly. tool_result bodies are the result
    // string (or stringified JSON if it was structured).
    //
    // Returns null if the file is missing or unparseable. Empty array if
    // the file exists but had no turns after the anchor.
    public static AgentTurn[]? TryReadLastTurnTranscript(string projectDir, string sessionId, string? promptHint = null)
    {
        var lines = TryLoadLines(projectDir, sessionId);
        if (lines is null) return null;

        var anchor = FindUserAnchor(lines, promptHint);

        var turns = new List<AgentTurn>();
        for (int i = Math.Max(0, anchor + 1); i < lines.Count; i++)
        {
            if (!TryParseEntry(lines[i], out _, out var blocks)) continue;
            foreach (var b in blocks)
            {
                var kind = b.Type switch
                {
                    "text"        => AgentTurnKind.Text,
                    "thinking"    => AgentTurnKind.Thinking,
                    "tool_use"    => AgentTurnKind.ToolUse,
                    "tool_result" => AgentTurnKind.ToolResult,
                    _             => (AgentTurnKind?)null,
                };
                if (kind is null) continue;
                turns.Add(new AgentTurn(kind.Value, b.Body));
            }
        }
        return turns.ToArray();
    }

    private static List<string>? TryLoadLines(string projectDir, string sessionId)
    {
        var path = PathFor(projectDir, sessionId);
        if (!File.Exists(path)) return null;
        try { return File.ReadAllLines(path).ToList(); }
        catch { return null; }
    }

    // Walk backward, find the index of the most recent user-text line.
    // If promptHint is given, prefer the line whose joined text contains it.
    // Returns -1 if no anchor — caller treats "everything" as post-anchor.
    private static int FindUserAnchor(List<string> lines, string? promptHint)
    {
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            if (!TryParseEntry(lines[i], out var role, out var blocks)) continue;
            if (role != "user") continue;
            var joined = string.Concat(blocks.Where(b => b.Type == "text").Select(b => b.Body));
            if (joined.Length == 0) continue;
            if (promptHint is null || joined.Contains(promptHint, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    // Parsed content block: kind ("text" / "thinking" / "tool_use" /
    // "tool_result" / something else) and its body. For "text"/"thinking"
    // the body is the raw string content; for "tool_use" the body is the
    // re-serialized {name, input} so the consumer can show name + args;
    // for "tool_result" the body is the result text/JSON.
    private readonly record struct Block(string Type, string Body);

    // Parse a single JSONL line into (role, [content blocks]).
    // Robust to lines that are missing fields or have non-array content.
    private static bool TryParseEntry(string line, out string role, out List<Block> blocks)
    {
        role = "";
        blocks = new();

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

    // Per-kind body extraction. Anything we don't know how to flatten gets
    // the raw JSON re-serialized so nothing is silently lost.
    private static string ExtractBody(string blockType, JsonElement block)
    {
        switch (blockType)
        {
            case "text":
                return block.TryGetProperty("text", out var tx) && tx.ValueKind == JsonValueKind.String
                    ? tx.GetString() ?? ""
                    : "";

            case "thinking":
                // Claude's extended-thinking block uses "thinking" for the
                // trace text. Some variants may use "text" — try both.
                if (block.TryGetProperty("thinking", out var th) && th.ValueKind == JsonValueKind.String)
                    return th.GetString() ?? "";
                if (block.TryGetProperty("text", out var thText) && thText.ValueKind == JsonValueKind.String)
                    return thText.GetString() ?? "";
                return "";

            case "tool_use":
            {
                // Surface { name, input } as JSON. The UI can pretty-print
                // or collapse; we keep full args per the "full fidelity"
                // call.
                var name  = block.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                            ? n.GetString() ?? "" : "";
                var input = block.TryGetProperty("input", out var inp) ? inp.GetRawText() : "{}";
                return $"{{\"name\":{JsonSerializer.Serialize(name)},\"input\":{input}}}";
            }

            case "tool_result":
            {
                // The result content may be a string OR an array of blocks
                // (Claude wraps multi-part results that way). Flatten to
                // string for display.
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
                // Unknown block type — preserve the raw JSON so nothing
                // is silently dropped. Diagnostic value > tidiness.
                return block.GetRawText();
        }
    }
}
