namespace RemoteAgents.Actors.Agents;

// The single "agent is blocked, needs an outside decision" seam — shared by permission
// Ask (a Choice) and agent questions. Pre-UI the only impl is the non-interactive stub.
public interface IDecisionResolver
{
    Task<string?> ResolveAsync(AgentQuestion question, CancellationToken ct);
}
