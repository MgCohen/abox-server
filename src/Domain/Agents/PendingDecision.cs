namespace RemoteAgents.Actors.Agents;

public sealed record PendingDecision(Guid Id, DecisionKind Kind, string Prompt, DateTimeOffset CreatedAt);
