using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ABox.Domain.Flow;
using ABox.Features.Flows.Shared;

namespace ABox.Features.Flows.Get;

public static class GetEndpoint
{
    public static void Map(IEndpointRouteBuilder flows) =>
        flows.MapGet("/{id:guid}", (Guid id, FlowRegistry runs, HttpContext http) =>
        {
            var snap = runs.Get(id);
            if (snap is null) return Results.NotFound();

            var etag = $"\"{snap.Version}\"";
            if ((string?)http.Request.Headers.IfNoneMatch == etag)
                return Results.StatusCode(StatusCodes.Status304NotModified);

            http.Response.Headers.ETag = etag;
            return Results.Ok(snap.ToView());
        });
}
