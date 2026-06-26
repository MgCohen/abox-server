namespace ABox.Features.Flows.Contract;

public sealed record FlowView(
    Guid Id,
    string Flow,
    string Project,
    string Phase,
    long Version,
    DateTimeOffset CreatedAt,
    IReadOnlyList<FlowOperationView> Operations,
    IReadOnlyList<FlowDecisionView> Decisions);
