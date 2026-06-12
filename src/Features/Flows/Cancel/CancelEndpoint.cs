using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ABox.Domain.Flow;

namespace ABox.Features.Flows.Cancel;

public static class CancelEndpoint
{
    public static void Map(IEndpointRouteBuilder flows) =>
        flows.MapPost("/{id:guid}/cancel", (Guid id, FlowRegistry runs) =>
            runs.Cancel(id) ? Results.Accepted() : Results.NotFound());
}
