using System.Text.Json;
using System.Text.Json.Nodes;

namespace RemoteAgents.Actors.Agents.Claude;

public static class ClaudePermission
{
    private const int DetailCap = 200;

    public static AgentQuestion.Choice ToQuestion(PermissionRequest request)
    {
        var (tool, detail) = Describe(request.Payload);
        var prompt = detail is null ? $"Allow `{tool}`?" : $"Allow `{tool}`: {detail} ?";
        return new AgentQuestion.Choice(prompt, ["Allow", "Deny"], AllowFreeText: false, request.Payload);
    }

    public static bool IsAllow(string? answer)
        => string.Equals(answer?.Trim(), "Allow", StringComparison.OrdinalIgnoreCase);

    public static string RenderResponse(bool allow, string reason)
    {
        var root = new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = "PreToolUse",
                ["permissionDecision"] = allow ? "allow" : "deny",
                ["permissionDecisionReason"] = reason,
            },
        };
        return root.ToJsonString();
    }

    private static (string Tool, string? Detail) Describe(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var tool = Prop(root, "tool_name")?.GetString() ?? "tool";
            return (tool, Detail(tool, Prop(root, "tool_input")));
        }
        catch (JsonException)
        {
            return ("tool", null);
        }
    }

    private static string? Detail(string tool, JsonElement? input)
    {
        if (input is not { ValueKind: JsonValueKind.Object } obj) return null;
        var key = tool switch
        {
            "Bash" => "command",
            "Write" or "Edit" or "MultiEdit" => "file_path",
            _ => null,
        };
        if (key is null || !obj.TryGetProperty(key, out var v) || v.ValueKind != JsonValueKind.String) return null;
        return Shorten(v.GetString());
    }

    private static string? Shorten(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length <= DetailCap ? s : s[..DetailCap] + "…";
    }

    private static JsonElement? Prop(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : null;
}
