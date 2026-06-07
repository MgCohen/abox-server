using System.Collections.Concurrent;

namespace RemoteAgents.Actors.Agents;

// A permission Choice self-answer is never "Allow", so Autonomous + Ask degrades to a
// safe deny — Autonomous agents gate via Auto/Bypass, not Ask (permission-interaction-model §2).
public sealed class AutoResolver : IDecisionResolver
{
    private const string ProceedInstruction =
        "No human is available to answer. Proceed autonomously: choose the most " +
        "reasonable, low-risk option, state the assumption you are making, and continue. " +
        "Do not ask again unless you truly cannot proceed.";

    // PROVISIONAL record: in-memory until the run history owns recorded assumptions.
    private readonly ConcurrentQueue<Assumption> _assumptions = new();

    public IReadOnlyCollection<Assumption> Assumptions => _assumptions;

    public Task<string?> ResolveAsync(AgentQuestion question, CancellationToken ct)
    {
        _assumptions.Enqueue(new Assumption(question.Prompt, ProceedInstruction));
        return Task.FromResult<string?>(ProceedInstruction);
    }

    public sealed record Assumption(string Question, string Answer);
}
