using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ABox.Domain.Flow;
using ABox.Features.Flows.Contracts;

namespace ABox.Features.Flows.Start;

public static class StartEndpoint
{
    public static void Map(IEndpointRouteBuilder flows) =>
        flows.MapPost("/", async (StartRunRequest req, FlowLauncher launcher, ProjectDirectory projects, CancellationToken ct) =>
        {
            string projectDir;
            try { projectDir = await projects.Resolve(req.Project, ct); }
            catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }

            var id = launcher.Start(req.Flow, req.Project, projectDir, req.Prompt);
            return id is null
                ? Results.NotFound(new { error = $"Unknown flow '{req.Flow}'." })
                : Results.Ok(new StartRunResponse(id.Value));
        });
}
