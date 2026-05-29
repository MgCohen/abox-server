using System.Text.Json;
using System.Text.RegularExpressions;

namespace RemoteAgents.Agents;

// Hook-line parser for Codex. Source tags this implementation recognizes
// (set by the append shim):
//
//   codex.permission_request    → TuiPrompt
//   codex.stop                  → OpenQuestion if last_assistant_message
//                                  matches the sentinel or interrogative
//                                  heuristic; otherwise null
//   codex.stop_failure          → null
//
// Codex has no dedicated "idle waiting on text input" event (unlike
// Claude's idle_prompt), so the orchestrator can only know a turn ended
// with a question by inspecting the assistant text in the Stop payload.
// The sentinel (<<NEEDS_INPUT>>, injected by the NonInteractive system
// prompt in step 5) is the high-confidence signal; the interrogative
// heuristic is the best-effort fallback for Interactive mode.
public sealed class CodexHookParser : IAgentHookParser
{
    public const string Sentinel = "<<NEEDS_INPUT>>";

    // Initial set — grow from real-run fixtures.
    private static readonly Regex InterrogativeLead = new(
        @"\b(Could you|Should I|Which|Do you want|How would you like|Would you prefer|Can you confirm)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public AgentQuestion? TryParse(JsonElement hookLine)
    {
        if (hookLine.ValueKind != JsonValueKind.Object) return null;
        if (!TryGetString(hookLine, "source", out var source)) return null;
        if (!hookLine.TryGetProperty("payload", out var payload)) return null;
        if (payload.ValueKind != JsonValueKind.Object) return null;

        switch (source)
        {
            case "codex.permission_request":
                return new AgentQuestion.TuiPrompt(
                    Text:        GetString(payload, "message"),
                    ToolName:    GetString(payload, "tool_name"),
                    ToolInput:   GetObjectOrEmpty(payload, "tool_input"),
                    HookPayload: payload.Clone(),
                    Source:      source);

            case "codex.stop":
                return ParseStop(payload);

            default:
                return null;
        }
    }

    private static AgentQuestion.OpenQuestion? ParseStop(JsonElement payload)
    {
        var lastMessage = GetString(payload, "last_assistant_message");
        if (string.IsNullOrWhiteSpace(lastMessage)) return null;

        var sentinelIdx = lastMessage.IndexOf(Sentinel, StringComparison.Ordinal);
        if (sentinelIdx >= 0)
        {
            var afterSentinel = lastMessage[(sentinelIdx + Sentinel.Length)..].Trim();
            return new AgentQuestion.OpenQuestion(
                Text:         afterSentinel.Length > 0 ? afterSentinel : lastMessage,
                FromSentinel: true,
                HookPayload:  payload.Clone(),
                Source:       "codex.stop.sentinel");
        }

        if (LooksLikeQuestion(lastMessage))
        {
            return new AgentQuestion.OpenQuestion(
                Text:         lastMessage,
                FromSentinel: false,
                HookPayload:  payload.Clone(),
                Source:       "codex.stop.heuristic");
        }

        return null;
    }

    // Public for unit-test access — pure function, no side effects.
    public static bool LooksLikeQuestion(string text)
    {
        var trimmed = text.TrimEnd();
        if (!trimmed.EndsWith('?')) return false;

        // Only consider the last paragraph — earlier "?" in the body
        // shouldn't promote a long completion message to a question.
        var lastBreak = trimmed.LastIndexOf("\n\n", StringComparison.Ordinal);
        var tail = lastBreak >= 0 ? trimmed[(lastBreak + 2)..] : trimmed;
        return InterrogativeLead.IsMatch(tail);
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
