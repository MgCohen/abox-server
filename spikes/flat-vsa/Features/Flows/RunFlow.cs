using App.Domain;
using App.Runtime;

namespace App.Features.Flows;

public sealed record RunFlowRequest(string Project);

public sealed record RunFlowResponse(Guid FlowId, string Status);

public sealed class RunFlowHandler(IFlowEngine engine, IEventBus bus) : IApiHandler<RunFlowRequest, RunFlowResponse>
{
    public async Task<RunFlowResponse> Handle(RunFlowRequest request, CancellationToken ct)
    {
        Flow flow = engine.Launch(request.Project);
        await bus.Publish(new FlowCompleted(flow.Id, flow.Project), ct);
        return new RunFlowResponse(flow.Id, flow.Status.ToString());
    }
}
