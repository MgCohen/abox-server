namespace ABox.Features.Flows.Contract;

public sealed record FlowOperationView(
    string Name,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    string? Summary,
    string? Error);
