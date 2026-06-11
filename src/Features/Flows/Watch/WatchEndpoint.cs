using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RemoteAgents.Domain.Flow;

namespace RemoteAgents.Features.Flows.Watch;

public static class WatchEndpoint
{
    public static void Map(IEndpointRouteBuilder flows) =>
        flows.MapGet("/{id:guid}/events", (Guid id, FlowRegistry runs, HttpContext http, CancellationToken ct) =>
            Sse.Stream(http, runs.Changes(id, ct), ct));
}
