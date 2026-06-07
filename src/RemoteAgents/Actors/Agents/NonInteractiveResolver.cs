namespace RemoteAgents.Actors.Agents;

// No human or config is available to answer, so a detected question is terminal:
// the flow surfaces NeedsInput and stops. Real auto-match / picker resolvers
// arrive with the interaction-modes + UI work.
public sealed class NonInteractiveResolver : IDecisionResolver
{
    public Task<string?> ResolveAsync(AgentQuestion question, DecisionKind kind, CancellationToken ct)
        => Task.FromResult<string?>(null);
}
