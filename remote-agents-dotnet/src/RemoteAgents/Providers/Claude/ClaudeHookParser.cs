using System.Text.Json;

namespace RemoteAgents.Agents;

// Hook-line parser for Claude Code. Source tags this implementation
// recognizes (set by the append shim):
//
//   claude.permission_prompt    → TuiPrompt   (tool-use modal)
//   claude.idle_prompt          → OpenQuestion (turn ended, waiting on text)
//   claude.elicitation_dialog   → OpenQuestion (MCP elicitation)
//   claude.stop / claude.stop_failure → null (clean turn end)
//
// Field paths follow Claude Code's documented Notification hook payload
// (see research/agent-hooks.md). Payloads evolve; this parser tolerates
// missing fields by emitting empty strings rather than throwing — the
// AgentQuestion record is always constructible from a recognized source.
public sealed class ClaudeHookParser : IAgentHookParser
{
    public AgentQuestion? TryParse(JsonElement hookLine)
    {
        if (hookLine.ValueKind != JsonValueKind.Object) return null;
        if (!TryGetString(hookLine, "source", out var source)) return null;
        if (!hookLine.TryGetProperty("payload", out var payload)) return null;
        if (payload.ValueKind != JsonValueKind.Object) return null;

        switch (source)
        {
            case "claude.permission_prompt":
                return new AgentQuestion.TuiPrompt(
                    Text:        GetString(payload, "message"),
                    ToolName:    GetString(payload, "tool_name"),
                    ToolInput:   GetObjectOrEmpty(payload, "tool_input"),
                    HookPayload: payload.Clone(),
                    Source:      source);

            case "claude.idle_prompt":
            case "claude.elicitation_dialog":
                return new AgentQuestion.OpenQuestion(
                    Text:         GetString(payload, "message"),
                    FromSentinel: false,
                    HookPayload:  payload.Clone(),
                    Source:       source);

            default:
                return null;
        }
    }

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
