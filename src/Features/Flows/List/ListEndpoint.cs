using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using RemoteAgents.Domain.Flow;
using RemoteAgents.Features.Flows.Shared;

namespace RemoteAgents.Features.Flows.List;

public static class ListEndpoint
{
    public static void Map(IEndpointRouteBuilder flows) =>
        flows.MapGet("/", (FlowRegistry runs) => runs.List().Select(s => s.ToView()));
}
