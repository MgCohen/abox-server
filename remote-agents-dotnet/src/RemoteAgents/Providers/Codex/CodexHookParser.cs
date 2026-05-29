using System.Text.Json;

namespace RemoteAgents.Agents;

// Hook-line parser for Codex. Source tags this implementation recognizes
// (set by the append shim):
//
//   codex.permission_request    → TuiPrompt
//   codex.stop                  → OpenQuestion via StopPayloadInspector
//                                  (sentinel or interrogative heuristic on
//                                  payload.last_assistant_message)
//   codex.stop_failure          → null
//
// The Stop-payload inspection is shared with ClaudeHookParser via
// StopPayloadInspector — both providers carry last_assistant_message
// under the same field name and benefit from the same detection rules.
public sealed class CodexHookParser : IAgentHookParser
{
    public const string Sentinel = UnattendedDirective.Sentinel;

    public AgentQuestion? TryParse(JsonElement hookLine)
    {
        if (hookLine.ValueKind != JsonValueKind.Object) return null;
        if (!TryGetString(hookLine, "source", out var source)) return null;
        if (!hookLine.TryGetProperty("payload", out var payload)) return null;
        if (payload.ValueKind != JsonValueKind.Object) return null;

        return source switch
        {
            "codex.permission_request" => new AgentQuestion.TuiPrompt(
                Text:        GetString(payload, "message"),
                ToolName:    GetString(payload, "tool_name"),
                ToolInput:   GetObjectOrEmpty(payload, "tool_input"),
                HookPayload: payload.Clone(),
                Source:      source),

            "codex.stop" => StopPayloadInspector.Inspect(
                payload,
                sentinelSource:  "codex.stop.sentinel",
                heuristicSource: "codex.stop.heuristic"),

            _ => null,
        };
    }

    // Kept for backwards-compat with existing test fixtures; new callers
    // should use StopPayloadInspector.LooksLikeQuestion directly.
    public static bool LooksLikeQuestion(string text) =>
        StopPayloadInspector.LooksLikeQuestion(text);

    private static bool TryGetString(JsonElement obj, string name, out string value)
    {
        if (obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
        {
            value = v.GetString() ?? "";
            return true;
        }
        value = "";
        return false;
    }

    private static string GetString(JsonElement obj, string name)
        => TryGetString(obj, name, out var v) ? v : "";

    private static JsonElement GetObjectOrEmpty(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object
            ? v.Clone()
            : JsonDocument.Parse("{}").RootElement.Clone();
}
