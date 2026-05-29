using System.Text.Json;
using System.Text.RegularExpressions;

namespace RemoteAgents.Agents;

// Shared logic for "did the agent end its turn with a question?" — both
// Claude's Notification:Stop and Codex's Stop hooks carry the assistant's
// final message under last_assistant_message, so the inspection rules are
// identical across providers. Each parser supplies its own source tags
// for the two detection paths (sentinel vs heuristic).
//
// InspectText is the same logic stripped of the JSON payload concern —
// any caller that already has the assistant's final text (e.g. CodexAgent
// reads it from `-o lastMessageFile` regardless of whether hooks fire)
// can apply the same detection as a fallback.
public static class StopPayloadInspector
{
    private static readonly Regex InterrogativeLead = new(
        @"\b(Could you|Should I|Which|Do you want|How would you like|Would you prefer|Can you confirm)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly JsonElement EmptyObject =
        JsonDocument.Parse("{}").RootElement.Clone();

    public static AgentQuestion.OpenQuestion? Inspect(
        JsonElement payload,
        string      sentinelSource,
        string      heuristicSource)
    {
        if (payload.ValueKind != JsonValueKind.Object) return null;

        var lastMessage = payload.GetStringOrEmpty("last_assistant_message");
        return InspectText(lastMessage, sentinelSource, heuristicSource, payload.Clone());
    }

    // Text-only path — for callers that have the assistant's final
    // message but no structured hook payload (e.g. CodexAgent reading
    // -o lastMessageFile when codex didn't fire hooks).
    public static AgentQuestion.OpenQuestion? InspectText(
        string text,
        string sentinelSource,
        string heuristicSource) =>
        InspectText(text, sentinelSource, heuristicSource, EmptyObject);

    private static AgentQuestion.OpenQuestion? InspectText(
        string      text,
        string      sentinelSource,
        string      heuristicSource,
        JsonElement payload)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var sentinelIdx = text.IndexOf(UnattendedDirective.Sentinel, StringComparison.Ordinal);
        if (sentinelIdx >= 0)
        {
            var afterSentinel = text[(sentinelIdx + UnattendedDirective.Sentinel.Length)..].Trim();
            return new AgentQuestion.OpenQuestion(
                Text:         afterSentinel.Length > 0 ? afterSentinel : text,
                FromSentinel: true,
                HookPayload:  payload,
                Source:       sentinelSource);
        }

        if (LooksLikeQuestion(text))
        {
            return new AgentQuestion.OpenQuestion(
                Text:         text,
                FromSentinel: false,
                HookPayload:  payload,
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

}
