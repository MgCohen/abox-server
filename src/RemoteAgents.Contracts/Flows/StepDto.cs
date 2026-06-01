namespace RemoteAgents.Contracts;

public sealed record StepDto(
    string Name,
    StepStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    string? Summary,
    string? Error);
