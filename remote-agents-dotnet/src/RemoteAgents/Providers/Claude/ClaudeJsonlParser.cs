using System.Text;
using System.Text.Json;
using RemoteAgents.Events;

namespace RemoteAgents.Agents;

// Pure parser for one line of Claude's per-session JSONL
// (~/.claude/projects/<encoded-cwd>/<session-id>.jsonl). Maps each
// meaningful content block onto AgentEvent chat variants. No I/O —
// callers handle file polling, partial-line resilience, and emission.
//
// Co-located with ClaudeJsonl.cs (the path/text-extraction helper) so
// every consumer of Claude's JSONL format lives in one folder.
public static class ClaudeJsonlParser
{
    // Parse a single JSONL line into 0..N AgentEvents. `agentName` is the
    // value to stamp on each emitted event (matches the running agent's
    // Name property). `defaultAt` is the timestamp to use when the line
    // has no `timestamp` field (rare — Claude stamps every entry).
    //
    // Returns empty for noise (malformed JSON, lines with no recognized
    // content, local-command stdin wrappers).
    public static IEnumerable<AgentEvent> ParseLine(string line, string agentName, DateTimeOffset defaultAt)
    {
        if (line.Length == 0 || line[0] != '{') yield break;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch (JsonException) { yield break; }

        using (doc)
        {
            var root = doc.RootElement;

            var uuid = root.TryGetProperty("uuid", out var u) && u.ValueKind == JsonValueKind.String
                ? u.GetString() ?? "" : "";
            var at = root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String
                ? DateTimeOffset.TryParse(ts.GetString(), out var parsed) ? parsed : defaultAt
                : defaultAt;

            var type = root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() ?? "" : "";

            if (type == "summary")
            {
                var summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "";
                yield return new AgentEvent.Meta(at, agentName, uuid, "summary", summary);
                yield break;
            }

            if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object)
                yield break;

            if (msg.TryGetProperty("role", out var r) && r.ValueKind == JsonValueKind.String)
                type = r.GetString() ?? type;

            if (type == "user" && root.TryGetProperty("isMeta", out var isMeta)
                && isMeta.ValueKind == JsonValueKind.True)
                yield break;

            if (!msg.TryGetProperty("content", out var content)) yield break;

            // Skip user messages that are local-command synthesised text
            // (e.g. /exit, /resume) — they're TUI plumbing, not real
            // conversation content the user wrote.
            if (type == "user" && content.ValueKind == JsonValueKind.String)
            {
                var raw = content.GetString() ?? "";
                if (IsLocalCommandWrapper(raw)) yield break;
            }

            if (content.ValueKind == JsonValueKind.String)
            {
                var txt = content.GetString() ?? "";
                if (txt.Length == 0) yield break;
                if (type == "user")
                    yield return new AgentEvent.UserText(at, agentName, uuid, txt);
                else if (type == "assistant")
                    yield return new AgentEvent.AssistantText(at, agentName, uuid, txt);
                yield break;
            }

            if (content.ValueKind != JsonValueKind.Array) yield break;

            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object) continue;
                var bt = block.TryGetProperty("type", out var btv) && btv.ValueKind == JsonValueKind.String
                    ? btv.GetString() ?? "" : "";

                switch (bt)
                {
                    case "text":
                        var text = block.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";
                        if (text.Length == 0) break;
                        if (type == "assistant")
                            yield return new AgentEvent.AssistantText(at, agentName, uuid, text);
                        else if (type == "user")
                            yield return new AgentEvent.UserText(at, agentName, uuid, text);
                        break;

                    case "thinking":
                        var think = block.TryGetProperty("thinking", out var th) ? th.GetString() ?? "" : "";
                        if (think.Length > 0)
                            yield return new AgentEvent.Thinking(at, agentName, uuid, think);
                        break;

                    case "tool_use":
                        var toolId = block.TryGetProperty("id", out var ti) ? ti.GetString() ?? "" : "";
                        var name = block.TryGetProperty("name", out var tn) ? tn.GetString() ?? "" : "";
                        var inputJson = block.TryGetProperty("input", out var ip) ? ip.GetRawText() : "{}";
                        yield return new AgentEvent.ToolUse(at, agentName, uuid, toolId, name, inputJson);
                        break;

                    case "tool_result":
                        var useId = block.TryGetProperty("tool_use_id", out var ui) ? ui.GetString() ?? "" : "";
                        var isErr = block.TryGetProperty("is_error", out var ie) && ie.ValueKind == JsonValueKind.True;
                        var rcontent = ExtractToolResultContent(block);
                        yield return new AgentEvent.ToolResult(at, agentName, uuid, useId, isErr, rcontent);
                        break;

                    default:
                        yield return new AgentEvent.Meta(at, agentName, uuid, bt, "");
                        break;
                }
            }
        }
    }

    private static bool IsLocalCommandWrapper(string text)
    {
        var t = text.TrimStart();
        return t.StartsWith("<command-name>")
            || t.StartsWith("<command-message>")
            || t.StartsWith("<command-args>")
            || t.StartsWith("<local-command-stdout>")
            || t.StartsWith("<local-command-stderr>")
            || t.StartsWith("<local-command-caveat>");
    }

    private static string ExtractToolResultContent(JsonElement block)
    {
        if (!block.TryGetProperty("content", out var c)) return "";
        if (c.ValueKind == JsonValueKind.String) return c.GetString() ?? "";
        if (c.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var item in c.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (item.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(t.GetString());
                }
            }
            return sb.ToString();
        }
        return c.GetRawText();
    }
}
