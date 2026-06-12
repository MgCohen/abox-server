namespace ABox.Domain.Agents;

// The single "agent is blocked, needs an outside decision" seam — shared by permission
// Ask (a Choice) and agent questions; DecisionKind tells them apart. Source labels who
// decided, for the run's decision ledger.
public interface IDecisionResolver
{
    Resolution Source { get; }

    Task<string?> ResolveAsync(AgentQuestion question, DecisionKind kind, CancellationToken ct);
}
