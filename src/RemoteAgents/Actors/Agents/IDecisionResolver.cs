namespace RemoteAgents.Actors.Agents;

// The single "agent is blocked, needs an outside decision" boundary — shared by
// permission `Ask` (a Choice) and agent-initiated questions. Pre-UI the only impl
// is the non-interactive stub; the UI swaps in a real human decider.
public interface IDecisionResolver
{
    Task<string?> ResolveAsync(AgentQuestion question, CancellationToken ct);
}
