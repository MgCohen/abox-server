using System.Text.Json;
using System.Text.RegularExpressions;

namespace RemoteAgents.Agents;

// Inspect the assistant's final message inside a Stop-hook payload and
// decide whether it represents a question. Both Claude's Notification:Stop
// and Codex's Stop hooks carry last_assistant_message under the same field
// name, so the rules are identical across providers. Each parser supplies
// its own source tags for the two detection paths (sentinel vs heuristic).
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

        var text = payload.GetStringOrEmpty("last_assistant_message");
        if (string.IsNullOrWhiteSpace(text)) return null;

        var sentinelIdx = text.IndexOf(UnattendedDirective.Sentinel, StringComparison.Ordinal);
        if (sentinelIdx >= 0)
        {
            var afterSentinel = text[(sentinelIdx + UnattendedDirective.Sentinel.Length)..].Trim();
            return new AgentQuestion.OpenQuestion(
                Text:         afterSentinel.Length > 0 ? afterSentinel : text,
                FromSentinel: true,
                HookPayload:  payload.Clone(),
                Source:       sentinelSource);
        }

        if (LooksLikeQuestion(text))
        {
            return new AgentQuestion.OpenQuestion(
                Text:         text,
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
}
