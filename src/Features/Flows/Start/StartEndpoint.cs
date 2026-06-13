using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ABox.Domain.Flow;
using ABox.Features.Flows.Contracts;
using ABox.Infrastructure.Projects;

namespace ABox.Features.Flows.Start;

public static class StartEndpoint
{
    public static void Map(IEndpointRouteBuilder flows) =>
        flows.MapPost("/", (StartRunRequest req, FlowLauncher launcher, IProjectRegistry projects) =>
        {
            string projectDir;
            try { projectDir = projects.Resolve(req.Project); }
            catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }

            var id = launcher.Start(req.Flow, req.Project, projectDir, req.Prompt);
            return id is null
                ? Results.NotFound(new { error = $"Unknown flow '{req.Flow}'." })
                : Results.Ok(new StartRunResponse(id.Value));
        });
}
