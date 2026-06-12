using ABox.Domain.Flow.Operations;

namespace ABox.Domain.Flow;

public sealed record FlowSnapshot(
    Guid Id,
    string Flow,
    string Project,
    FlowPhase Phase,
    long Version,
    DateTimeOffset CreatedAt,
    IReadOnlyList<OperationDto> Operations,
    IReadOnlyList<DecisionDto> Decisions);
