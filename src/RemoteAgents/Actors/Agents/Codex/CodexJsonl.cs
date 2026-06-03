using System.Text.Json;

namespace RemoteAgents.Actors.Agents.Codex;

public static class CodexJsonl
{
    public static AgentTurn[] ExtractTranscript(string rawStdout)
    {
        if (string.IsNullOrWhiteSpace(rawStdout)) return [];

        var turns = new List<AgentTurn>();
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
                if (!root.TryGetProperty("type", out var t) || t.ValueKind != JsonValueKind.String || t.GetString() != "item.completed") continue;
                if (!root.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("type", out var itType) || itType.ValueKind != JsonValueKind.String) continue;

                switch (itType.GetString())
                {
                    case "agent_message":
                        if (item.TryGetProperty("text", out var msgText) && msgText.ValueKind == JsonValueKind.String)
                            turns.Add(new AgentTurn(AgentTurnKind.Text, msgText.GetString() ?? ""));
                        break;

                    case "command_execution":
                        var cmd = item.TryGetProperty("command", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : "";
                        turns.Add(new AgentTurn(AgentTurnKind.ToolUse,
                            $"{{\"name\":\"shell\",\"input\":{{\"command\":{JsonSerializer.Serialize(cmd)}}}}}"));
                        var output = item.TryGetProperty("aggregated_output", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString() ?? "" : "";
                        var exit = item.TryGetProperty("exit_code", out var ec) && ec.ValueKind == JsonValueKind.Number ? ec.GetInt32().ToString() : "?";
                        turns.Add(new AgentTurn(AgentTurnKind.ToolResult, $"[exit {exit}]\n{output}"));
                        break;

                    case "reasoning":
                        var body = item.TryGetProperty("text", out var rt) && rt.ValueKind == JsonValueKind.String
                            ? rt.GetString() ?? ""
                            : item.TryGetProperty("summary", out var rs) && rs.ValueKind == JsonValueKind.String ? rs.GetString() ?? "" : "";
                        if (!string.IsNullOrEmpty(body))
                            turns.Add(new AgentTurn(AgentTurnKind.Thinking, body));
                        break;
                }
            }
        }
        return [.. turns];
    }
}
