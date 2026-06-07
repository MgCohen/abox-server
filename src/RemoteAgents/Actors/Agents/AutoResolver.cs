using System.Collections.Concurrent;

namespace RemoteAgents.Actors.Agents;

// The decider for an Autonomous agent: it never blocks. Any question is self-answered
// with an instruction to proceed on a best assumption, and the assumption is recorded.
// For a permission Choice the answer is not "Allow", so it degrades to a safe deny —
// Autonomous agents are expected to gate via Auto/Bypass, not Ask (Ask presumes
// Interactive; permission-interaction-model §2).
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
