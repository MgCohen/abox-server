namespace RemoteAgents.Contracts;

/// <summary>One step in a flow snapshot, as the UI renders it.</summary>
public sealed record StepDto(
    string Name,
    StepStatus Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    string? Summary,
    string? Error);
