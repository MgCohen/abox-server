namespace RemoteAgents.Actors.Agents;

// The single "agent is blocked, needs an outside decision" seam — shared by permission
// Ask (a Choice) and agent questions; DecisionKind tells them apart.
public interface IDecisionResolver
{
    Task<string?> ResolveAsync(AgentQuestion question, DecisionKind kind, CancellationToken ct);
}
