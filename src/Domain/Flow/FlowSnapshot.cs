using RemoteAgents.Domain.Flow.Operations;

namespace RemoteAgents.Domain.Flow;

public sealed record FlowSnapshot(
    Guid Id,
    string Flow,
    string Project,
    FlowPhase Phase,
    long Version,
    DateTimeOffset CreatedAt,
    IReadOnlyList<OperationDto> Operations,
    IReadOnlyList<DecisionDto> Decisions);
