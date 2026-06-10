namespace RemoteAgents.Actors.Agents;

// Parks the decision in the registry and blocks until a human answers it or the run
// is cancelled (token trip ⇒ null ⇒ terminal NeedsInput). Pre-UI a scripted
// fulfiller drives the registry; the fulfill endpoint (POST /runs/{runId}/decisions/{id})
// and the attention inbox call PendingDecisions.Resolve in exactly the same way.
public sealed class InteractiveResolver(PendingDecisions pending) : IDecisionResolver
{
    public Resolution Source => Resolution.Human;

    public Task<string?> ResolveAsync(AgentQuestion question, DecisionKind kind, CancellationToken ct)
    {
        var decision = new PendingDecision(Guid.NewGuid(), kind, question.Prompt, DateTimeOffset.UtcNow);
        return pending.Register(decision, ct);
    }
}
