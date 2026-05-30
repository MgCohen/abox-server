using System.Text.Json;
using RemoteAgents.Agents;

namespace RemoteAgents.Providers.Codex;

// Parse the codex --json event stream into the same AgentTurn vocabulary
// Claude uses. codex emits one JSON object per line on stdout interleaved
// with non-JSON tracing (codex_core::exec ERROR lines), so the parser
// skips anything that doesn't start with '{'.
//
// Event shapes observed (codex-cli 0.134.0):
//   { type: "thread.started",   thread_id }            — skipped
//   { type: "turn.started" }                           — skipped
//   { type: "turn.completed",   usage }                — skipped
//   { type: "turn.failed",      error }                — skipped
//   { type: "error",            message }              — skipped (handled elsewhere)
//   { type: "item.started",     item }                 — skipped (preview of in-progress)
//   { type: "item.completed",   item }                 — translated
//
// item.type values translated:
//   "agent_message"      → Text         (body = item.text)
//   "command_execution"  → ToolUse + ToolResult
//                            ToolUse body  = {name:"shell", input:{command}}
//                            ToolResult body = item.aggregated_output
//   "reasoning"          → Thinking     (body = item.text or item.summary)
//
// Unknown item types are skipped — diagnostic over crash, keeps the
// transcript clean if a future codex version adds a new item type.
public static class CodexJsonl
{
    public static AgentTurn[] ExtractTranscript(string rawStdout)
    {
        if (string.IsNullOrWhiteSpace(rawStdout)) return Array.Empty<AgentTurn>();

        var turns = new List<AgentTurn>();
        // Split on either CRLF or LF — codex on Windows emits CRLF.
        foreach (var rawLine in rawStdout.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').TrimStart();
            if (line.Length == 0 || line[0] != '{') continue;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch { continue; }

            using (doc)
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String) continue;
                if (t.GetString() != "item.completed") continue;
                if (!root.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("type", out var itType) || itType.ValueKind != JsonValueKind.String) continue;

                switch (itType.GetString())
                {
                    case "agent_message":
                        if (item.TryGetProperty("text", out var msgText) && msgText.ValueKind == JsonValueKind.String)
                            turns.Add(new AgentTurn(AgentTurnKind.Text, msgText.GetString() ?? ""));
                        break;

                    case "command_execution":
                    {
                        var cmd = item.TryGetProperty("command", out var c) && c.ValueKind == JsonValueKind.String
                            ? c.GetString() ?? "" : "";
                        // Surface the command as a ToolUse with a stable
                        // schema matching the Claude side: {name, input}.
                        var useBody = $"{{\"name\":\"shell\",\"input\":{{\"command\":{JsonSerializer.Serialize(cmd)}}}}}";
                        turns.Add(new AgentTurn(AgentTurnKind.ToolUse, useBody));

                        // Output + exit code → ToolResult.
                        var output = item.TryGetProperty("aggregated_output", out var o) && o.ValueKind == JsonValueKind.String
                            ? o.GetString() ?? "" : "";
                        var exit   = item.TryGetProperty("exit_code", out var ec) && ec.ValueKind == JsonValueKind.Number
                            ? ec.GetInt32().ToString()
                            : "?";
                        turns.Add(new AgentTurn(AgentTurnKind.ToolResult,
                            $"[exit {exit}]\n{output}"));
                        break;
                    }

                    case "reasoning":
                    {
                        // Codex may surface reasoning under either "text" or
                        // "summary"; try both.
                        var body = item.TryGetProperty("text", out var rt) && rt.ValueKind == JsonValueKind.String
                            ? rt.GetString() ?? ""
                            : (item.TryGetProperty("summary", out var rs) && rs.ValueKind == JsonValueKind.String
                                ? rs.GetString() ?? "" : "");
                        if (!string.IsNullOrEmpty(body))
                            turns.Add(new AgentTurn(AgentTurnKind.Thinking, body));
                        break;
                    }

                    // Unknown item types ignored — see header.
                }
            }
        }
        return turns.ToArray();
    }
}
