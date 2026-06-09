using Domain;
using Flows.Contracts;
using Infra.AgentRuntime;

namespace Flows.GetFlowSnapshot;

internal sealed class GetFlowSnapshotHandler(IFlowEngine engine)
    : IApiHandler<GetFlowSnapshotRequest, FlowSnapshotDto?>
{
    public Task<FlowSnapshotDto?> Handle(GetFlowSnapshotRequest request, CancellationToken ct)
    {
        Flow? flow = engine.Find(request.FlowId);
        FlowSnapshotDto? dto = flow is null
            ? null
            : new FlowSnapshotDto(flow.Id, flow.Project, flow.Status.ToString(),
                flow.Phases.Select(MapPhase).ToList());
        return Task.FromResult(dto);
    }

    private static FlowPhaseDto MapPhase(FlowPhase phase) => new(phase.Name, phase.State.ToString());
}
