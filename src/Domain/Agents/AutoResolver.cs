namespace RemoteAgents.Domain.Agents;

// A permission Choice self-answer is never "Allow", so Auto + Ask degrades to a
// safe deny — autonomous agents gate via Auto/Bypass policy, not Ask (permission-interaction-model §2).
// The proceed instruction it returns is recorded on the run ledger by the Agent loop.
public sealed class AutoResolver : IDecisionResolver
{
    private const string ProceedInstruction =
        "No human is available to answer. Proceed autonomously: choose the most " +
        "reasonable, low-risk option, state the assumption you are making, and continue. " +
        "Do not ask again unless you truly cannot proceed.";

    public Resolution Source => Resolution.Auto;

    public Task<string?> ResolveAsync(AgentQuestion question, DecisionKind kind, CancellationToken ct)
        => Task.FromResult<string?>(ProceedInstruction);
}
