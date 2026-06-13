namespace ABox.Features.Flows.Contracts;

public sealed record FlowOperationView(
    string Name,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    string? Summary,
    string? Error);
