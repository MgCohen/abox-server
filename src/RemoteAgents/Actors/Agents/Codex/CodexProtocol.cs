using System.Text.Json;

namespace RemoteAgents.Actors.Agents.Codex;

public static class CodexProtocol
{
    public static List<string> BuildArgs(string? sessionId, string projectDir, string lastMessageFile, string model, string sandbox)
    {
        var args = new List<string> { "exec" };
        if (sessionId is not null) { args.Add("resume"); args.Add(sessionId); }

        args.Add("--cd"); args.Add(projectDir);
        args.Add("-o"); args.Add(lastMessageFile);
        args.Add("--sandbox"); args.Add(sandbox);
        // codex refuses to run unattended in a non-git or first-seen dir without this.
        args.Add("--skip-git-repo-check");
        args.Add("--json");
        if (!string.IsNullOrEmpty(model)) { args.Add("--model"); args.Add(model); }
        args.Add("-");
        return args;
    }

    public static string? ScanSessionId(string line)
    {
        if (!TryParseObject(line, out var doc)) return null;
        using (doc)
        {
            var root = doc.RootElement;

            foreach (var key in new[] { "thread_id", "session_id", "sessionId" })
                if (StringProp(root, key) is { } s && IsId(s)) return s;

            foreach (var (parent, child) in new[] { ("thread", "id"), ("session", "id") })
                if (Obj(root, parent) is { } p && StringProp(p, child) is { } s && IsId(s)) return s;

            if (Obj(root, "payload") is { } payload)
                foreach (var key in new[] { "thread_id", "session_id" })
                    if (StringProp(payload, key) is { } s && IsId(s)) return s;
        }
        return null;
    }

    public static AgentTurn[] ExtractTranscript(string rawStdout)
    {
        if (string.IsNullOrWhiteSpace(rawStdout)) return [];

        var turns = new List<AgentTurn>();
        foreach (var line in rawStdout.Split('\n'))
        {
            if (!TryParseObject(line, out var doc)) continue;
            using (doc)
            {
                var root = doc.RootElement;
                if (StringProp(root, "type") != "item.completed") continue;
                if (Obj(root, "item") is not { } item) continue;

                switch (StringProp(item, "type"))
                {
                    case "agent_message": AddText(turns, item); break;
                    case "command_execution": AddCommand(turns, item); break;
                    case "reasoning": AddReasoning(turns, item); break;
                }
            }
        }
        return [.. turns];
    }

    private static void AddText(List<AgentTurn> turns, JsonElement item)
    {
        if (StringProp(item, "text") is { } text)
            turns.Add(new AgentTurn(AgentTurnKind.Text, text));
    }

    private static void AddCommand(List<AgentTurn> turns, JsonElement item)
    {
        var command = StringProp(item, "command") ?? "";
        turns.Add(new AgentTurn(AgentTurnKind.ToolUse,
            $"{{\"name\":\"shell\",\"input\":{{\"command\":{JsonSerializer.Serialize(command)}}}}}"));

        var output = StringProp(item, "aggregated_output") ?? "";
        var exit = item.TryGetProperty("exit_code", out var ec) && ec.ValueKind == JsonValueKind.Number
            ? ec.GetInt32().ToString()
            : "?";
        turns.Add(new AgentTurn(AgentTurnKind.ToolResult, $"[exit {exit}]\n{output}"));
    }

    private static void AddReasoning(List<AgentTurn> turns, JsonElement item)
    {
        var body = StringProp(item, "text") ?? StringProp(item, "summary");
        if (!string.IsNullOrEmpty(body))
            turns.Add(new AgentTurn(AgentTurnKind.Thinking, body));
    }

    private static bool TryParseObject(string line, out JsonDocument doc)
    {
        doc = null!;
        line = line.Trim();
        if (line.Length == 0 || line[0] != '{') return false;
        try { doc = JsonDocument.Parse(line); return true; }
        catch { return false; }
    }

    private static string? StringProp(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static JsonElement? Obj(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    private static bool IsId(string s) => s.Length >= 8;
}
