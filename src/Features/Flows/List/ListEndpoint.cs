using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using ABox.Domain.Flow;
using ABox.Features.Flows.Shared;

namespace ABox.Features.Flows.List;

public static class ListEndpoint
{
    public static void Map(IEndpointRouteBuilder flows) =>
        flows.MapGet("/", (FlowRegistry runs) => runs.List().Select(s => s.ToView()));
}
