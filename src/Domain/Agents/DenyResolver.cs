namespace ABox.Domain.Agents;

// Refuses every decision. A permission Ask returns an explicit deny — the reject
// option, by convention the last one (e.g. ["Allow", "Deny"]) — so it's recorded
// as a real choice rather than an abstention; anything else gets null.
public sealed class DenyResolver : IDecisionResolver
{
    public Resolution Source => Resolution.Deny;

    public Task<string?> ResolveAsync(AgentQuestion question, DecisionKind kind, CancellationToken ct)
    {
        string? answer = kind == DecisionKind.Permission && question is AgentQuestion.Choice { Options: [.., var deny] }
            ? deny
            : null;
        return Task.FromResult(answer);
    }
}
