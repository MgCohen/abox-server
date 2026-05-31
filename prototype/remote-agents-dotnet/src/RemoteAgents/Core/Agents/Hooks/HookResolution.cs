using System.Text.Json;

namespace RemoteAgents.Agents.Hooks;

// Reads <sessionDir>/hooks.jsonl, runs each line through the provider's
// parser, and maps the first detected question + the run's InteractionMode
// onto AgentStatus / Question / FailureReason. Pure I/O + dispatch — no
// agent-specific knowledge.
//
// "First detected question wins" per PLANS/interaction-modes.md §7. In
// practice the agent pauses at its first NeedsInput signal anyway, so
// first ≈ last for realistic turns.
public static class HookResolution
{
    public sealed record Outcome(AgentStatus Status, AgentQuestion? Question, string? FailureReason);

    public static Outcome FromHooksJsonl(
        string?           hooksJsonlPath,
        IAgentHookParser  parser,
        InteractionMode   mode)
    {
        if (string.IsNullOrEmpty(hooksJsonlPath) || !File.Exists(hooksJsonlPath))
            return Completed;

        var question = FirstQuestion(hooksJsonlPath, parser);
        return question is null ? Completed : ForQuestion(question, mode);
    }

    // Map a detected question + the run's mode onto an outcome. Used by
    // FromHooksJsonl and by providers that detect questions through other
    // channels (e.g. CodexAgent's result.Text sentinel fallback).
    public static Outcome ForQuestion(AgentQuestion question, InteractionMode mode) =>
        mode == InteractionMode.NonInteractive
            ? new Outcome(
                AgentStatus.Failed,
                question,
                $"agent asked a question in non-interactive mode ({question.Source}): {question.Text}")
            : new Outcome(AgentStatus.NeedsInput, question, null);

    private static AgentQuestion? FirstQuestion(string path, IAgentHookParser parser)
    {
        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument doc;
            try { doc = JsonDocument.Parse(line); }
            catch { continue; }

            using (doc)
            {
                var q = parser.TryParse(doc.RootElement);
                if (q is not null) return q;
            }
        }
        return null;
    }

    // The "nothing to report" outcome — no hooks configured, or hooks.jsonl
    // held no question. Public so Agent can use it on the no-hooks path
    // without synthesizing a parser.
    public static readonly Outcome Completed = new(AgentStatus.Completed, null, null);
}
