namespace Flows.Contracts;

public sealed record GetFlowSnapshotRequest(Guid FlowId);

public sealed record FlowPhaseDto(string Name, string State);

public sealed record FlowSnapshotDto(Guid Id, string Project, string Status, IReadOnlyList<FlowPhaseDto> Phases);
