using System.Text.Json;

namespace RemoteAgents.Actors.Agents.Codex;

public static class CodexSessionId
{
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
                if (root.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && IsId(v.GetString()))
                    return v.GetString();

            foreach (var (parent, child) in new[] { ("thread", "id"), ("session", "id") })
                if (root.TryGetProperty(parent, out var p) && p.ValueKind == JsonValueKind.Object &&
                    p.TryGetProperty(child, out var v) && v.ValueKind == JsonValueKind.String && IsId(v.GetString()))
                    return v.GetString();

            if (root.TryGetProperty("payload", out var payload) && payload.ValueKind == JsonValueKind.Object)
                foreach (var key in new[] { "thread_id", "session_id" })
                    if (payload.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String && IsId(v.GetString()))
                        return v.GetString();
        }
        return null;
    }

    private static bool IsId(string? s) => s is { Length: >= 8 };
}
