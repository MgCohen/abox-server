namespace RemoteAgents.Domain.Flow.Operations;

public sealed record OperationDto(
    string Name,
    OperationStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    string? Summary,
    string? Error);
