using System.Text.Json;

namespace RemoteAgents.Agents;

// Hook-line parser for Claude Code. Source tags this implementation
// recognizes (set by the append shim):
//
//   claude.permission_prompt    → TuiPrompt   (tool-use modal)
//   claude.idle_prompt          → OpenQuestion (turn ended, waiting on text;
//                                  only fires when the TUI stays alive past
//                                  the turn — doesn't trigger under our
//                                  /exit-driven linear flow)
//   claude.elicitation_dialog   → OpenQuestion (MCP elicitation)
//   claude.stop                 → OpenQuestion via StopPayloadInspector,
//                                  shared with Codex — payload carries
//                                  last_assistant_message which is where the
//                                  sentinel and interrogative heuristics
//                                  actually fire under our flow
//   claude.stop_failure         → null
//
// Real-run smoke (sessions/...-smoke-question-detection/) confirmed Stop
// is the dominant signal under our orchestrator's "type prompt → /exit"
// flow; idle_prompt and elicitation_dialog only fire if the TUI is left
// alive — they're kept here for completeness against future flows that
// don't /exit.
public sealed class ClaudeHookParser : IAgentHookParser
{
    public AgentQuestion? TryParse(JsonElement hookLine)
    {
        if (hookLine.ValueKind != JsonValueKind.Object) return null;
        if (!TryGetString(hookLine, "source", out var source)) return null;
        if (!hookLine.TryGetProperty("payload", out var payload)) return null;
        if (payload.ValueKind != JsonValueKind.Object) return null;

        return source switch
        {
            "claude.permission_prompt" => new AgentQuestion.TuiPrompt(
                Text:        GetString(payload, "message"),
                ToolName:    GetString(payload, "tool_name"),
                ToolInput:   GetObjectOrEmpty(payload, "tool_input"),
                HookPayload: payload.Clone(),
                Source:      source),

            "claude.idle_prompt" or "claude.elicitation_dialog" =>
                new AgentQuestion.OpenQuestion(
                    Text:         GetString(payload, "message"),
                    FromSentinel: false,
                    HookPayload:  payload.Clone(),
                    Source:       source),

            "claude.stop" => StopPayloadInspector.Inspect(
                payload,
                sentinelSource:  "claude.stop.sentinel",
                heuristicSource: "claude.stop.heuristic"),

            _ => null,
        };
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
