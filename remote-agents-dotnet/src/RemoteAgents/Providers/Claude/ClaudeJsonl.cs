using RemoteAgents.Agents;
using System.Text;
using System.Text.Json;
using RemoteAgents.Events;

namespace RemoteAgents.Providers.Claude;

// Reads the per-session JSONL Claude Code writes to
// `~/.claude/projects/<encoded-cwd>/<sessionId>.jsonl`.
//
// We use this as the authoritative source for "what did Claude say this
// turn" — the ANSI-stripped PTY buffer works most of the time but breaks
// when the TUI wraps the prompt across cells, drops trailing bytes if the
// reader is canceled mid-chunk, or scrolls real content out of view.
// The JSONL has none of those problems: one JSON object per line, schema
// shared with Claude's own resume machinery.
//
// Public so flows can call it themselves if they want to read past turns,
// but ClaudeAgent uses it as its primary text source.
public static class ClaudeJsonl
{
    // Resolve the path Claude Code writes to for (projectDir, sessionId).
    // Returns the path even if the file doesn't exist yet — caller can
    // probe with File.Exists.
    public static string PathFor(string projectDir, string sessionId)
    {
        var encoded = ProviderJsonlIngestSink.EncodeCwd(projectDir);
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
        var path = PathFor(projectDir, sessionId);
        if (!File.Exists(path)) return null;

        List<string> lines;
        try { lines = File.ReadAllLines(path).ToList(); }
        catch { return null; }

        // Two-pass:
        //  1. find the index of the most recent user-text line matching the
        //     promptHint (or just the most recent user-text line as fallback)
        //  2. concatenate every assistant text block AFTER that index
        int anchor = -1;
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            if (!TryParseEntry(lines[i], out var role, out var texts, out _)) continue;
            if (role != "user") continue;
            if (texts.Count == 0) continue;

            var joined = string.Concat(texts);
            if (promptHint is null || joined.Contains(promptHint, StringComparison.Ordinal))
            {
                anchor = i;
                break;
            }
        }

        var sb = new StringBuilder();
        for (int i = Math.Max(0, anchor + 1); i < lines.Count; i++)
        {
            if (!TryParseEntry(lines[i], out var role, out var texts, out _)) continue;
            if (role != "assistant") continue;
            foreach (var t in texts)
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(t);
            }
        }

        return sb.ToString();
    }

    // Parse a single JSONL line into (role, text-content-blocks, raw-content-types).
    // Robust to lines that are missing fields or have non-array content.
    private static bool TryParseEntry(string line, out string role, out List<string> textBlocks, out List<string> contentTypes)
    {
        role = "";
        textBlocks = new();
        contentTypes = new();

        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{') return false;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch { return false; }

        using (doc)
        {
            var root = doc.RootElement;
            // Top-level "type" carries the role for the orchestrator's purposes.
            if (root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String)
                role = t.GetString() ?? "";

            if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object)
                return true; // role known, no content

            // message.role overrides top-level type if present (in practice
            // they agree — but defend anyway).
            if (msg.TryGetProperty("role", out var r) && r.ValueKind == JsonValueKind.String)
                role = r.GetString() ?? role;

            if (!msg.TryGetProperty("content", out var content)) return true;

            if (content.ValueKind == JsonValueKind.String)
            {
                contentTypes.Add("text");
                textBlocks.Add(content.GetString() ?? "");
                return true;
            }
            if (content.ValueKind != JsonValueKind.Array) return true;

            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object) continue;
                var bt = "";
                if (block.TryGetProperty("type", out var btv) && btv.ValueKind == JsonValueKind.String)
                    bt = btv.GetString() ?? "";
                contentTypes.Add(bt);
                if (bt == "text" && block.TryGetProperty("text", out var tx) && tx.ValueKind == JsonValueKind.String)
                    textBlocks.Add(tx.GetString() ?? "");
            }
            return true;
        }
    }
}
