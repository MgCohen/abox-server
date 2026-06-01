namespace RemoteAgents.Contracts;

public sealed record FlowSnapshot(
    Guid Id,
    string Flow,
    string Project,
    FlowPhase Phase,
    long Version,
    DateTimeOffset CreatedAt,
    IReadOnlyList<StepDto> Steps);
