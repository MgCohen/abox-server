using App.Domain;
using App.Runtime;

namespace App.Features.Flows;

public sealed record GetFlowSnapshotRequest(Guid FlowId);

public sealed record FlowPhaseDto(string Name, string State);

public sealed record FlowSnapshotDto(Guid Id, string Project, string Status, IReadOnlyList<FlowPhaseDto> Phases);

public sealed class GetFlowSnapshotHandler(IFlowEngine engine) : IApiHandler<GetFlowSnapshotRequest, FlowSnapshotDto?>
{
    public Task<FlowSnapshotDto?> Handle(GetFlowSnapshotRequest request, CancellationToken ct)
    {
        Flow? flow = engine.Find(request.FlowId);
        FlowSnapshotDto? dto = flow is null
            ? null
            : new FlowSnapshotDto(flow.Id, flow.Project, flow.Status.ToString(),
                flow.Phases.Select(p => new FlowPhaseDto(p.Name, p.State.ToString())).ToList());
        return Task.FromResult(dto);
    }
}
