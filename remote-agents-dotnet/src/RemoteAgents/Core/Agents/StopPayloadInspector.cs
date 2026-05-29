using System.Text.Json;
using System.Text.RegularExpressions;

namespace RemoteAgents.Agents;

// Shared logic for "did the agent end its turn with a question?" — both
// Claude's Notification:Stop and Codex's Stop hooks carry the assistant's
// final message under last_assistant_message, so the inspection rules are
// identical across providers. Each parser supplies its own source tags
// for the two detection paths (sentinel vs heuristic).
//
// Real-run smoke (sessions/...-smoke-question-detection/) confirmed Stop
// is the practical signal under our linear "type prompt → /exit" flow —
// idle_prompt never fires because we don't keep the TUI alive after the
// turn ends. The sentinel path is the high-confidence channel
// (NonInteractive mode injects the directive that asks the model to emit
// <<NEEDS_INPUT>>); the heuristic is best-effort for Interactive runs.
public static class StopPayloadInspector
{
    private static readonly Regex InterrogativeLead = new(
        @"\b(Could you|Should I|Which|Do you want|How would you like|Would you prefer|Can you confirm)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static AgentQuestion.OpenQuestion? Inspect(
        JsonElement payload,
        string      sentinelSource,
        string      heuristicSource)
    {
        if (payload.ValueKind != JsonValueKind.Object) return null;

        var lastMessage = GetString(payload, "last_assistant_message");
        if (string.IsNullOrWhiteSpace(lastMessage)) return null;

        var sentinelIdx = lastMessage.IndexOf(UnattendedDirective.Sentinel, StringComparison.Ordinal);
        if (sentinelIdx >= 0)
        {
            var afterSentinel = lastMessage[(sentinelIdx + UnattendedDirective.Sentinel.Length)..].Trim();
            return new AgentQuestion.OpenQuestion(
                Text:         afterSentinel.Length > 0 ? afterSentinel : lastMessage,
                FromSentinel: true,
                HookPayload:  payload.Clone(),
                Source:       sentinelSource);
        }

        if (LooksLikeQuestion(lastMessage))
        {
            return new AgentQuestion.OpenQuestion(
                Text:         lastMessage,
                FromSentinel: false,
                HookPayload:  payload.Clone(),
                Source:       heuristicSource);
        }

        return null;
    }

    // Public — exercised directly by parser unit tests.
    public static bool LooksLikeQuestion(string text)
    {
        var trimmed = text.TrimEnd();
        if (!trimmed.EndsWith('?')) return false;

        // Only the last paragraph counts — an interrogative lead earlier
        // in a long completion message shouldn't promote it to a question.
        var lastBreak = trimmed.LastIndexOf("\n\n", StringComparison.Ordinal);
        var tail = lastBreak >= 0 ? trimmed[(lastBreak + 2)..] : trimmed;
        return InterrogativeLead.IsMatch(tail);
    }

    private static string GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";
}
