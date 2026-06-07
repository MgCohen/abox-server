namespace RemoteAgents.Actors.Agents;

// Refuses every decision. A permission Ask gets an explicit deny — its reject
// option, which by convention is the last one (e.g. ["Allow", "Deny"]) — so the
// refusal is recorded as a real choice, not an abstention. An open question gets
// null: "I won't decide; stop." (This is the one behavior that distinguishes it
// from NonInteractiveResolver, which abstains on everything.)
public sealed class DenyResolver : IDecisionResolver
{
    public Task<string?> ResolveAsync(AgentQuestion question, DecisionKind kind, CancellationToken ct)
    {
        string? answer = kind == DecisionKind.Permission && question is AgentQuestion.Choice { Options: [.., var deny] }
            ? deny
            : null;
        return Task.FromResult(answer);
    }
}
