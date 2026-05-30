using System.Text.Json;

namespace RemoteAgents.Agents;

// Codex's session-id parser. Codex has emitted the id under several
// different field shapes across versions — this class walks the known
// shapes on each JSON line until one matches. Lives next to the Claude
// JSONL parsers (ClaudeJsonl / ClaudeJsonlParser) so CodexAgent only
// owns the drive loop.
public static class CodexSessionId
{
    // Scan a single JSON line for any of the session-id field shapes
    // codex has used across versions. Returns null if none found.
    public static string? Scan(string line)
    {
        line = line.Trim();
        if (line.Length == 0 || line[0] != '{') return null;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch { return null; }

        using (doc)
        {
            var root = doc.RootElement;
            foreach (var key in new[] { "thread_id", "session_id", "sessionId" })
            {
                if (root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString();
                    if (s is not null && s.Length >= 8) return s;
                }
            }
            foreach (var (parent, child) in new[] { ("thread", "id"), ("session", "id") })
            {
                if (root.TryGetProperty(parent, out var p) && p.ValueKind == JsonValueKind.Object &&
                    p.TryGetProperty(child, out var v) && v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString();
                    if (s is not null && s.Length >= 8) return s;
                }
            }
            if (root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "thread_id", "session_id" })
                {
                    if (payload.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                    {
                        var s = v.GetString();
                        if (s is not null && s.Length >= 8) return s;
                    }
                }
            }
        }
        return null;
    }
}
