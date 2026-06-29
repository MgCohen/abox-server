using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ABox.Domain.Flow;
using ABox.Domain.Projects;
using ABox.Features.Flows.Contract;

namespace ABox.Features.Flows.Start;

public static class StartEndpoint
{
    public static void Map(IEndpointRouteBuilder flows) =>
        flows.MapPost("/", async (StartRunRequest req, FlowLauncher launcher, ProjectResolver projects, CancellationToken ct) =>
        {
            Project project;
            try { project = await projects.Resolve(req.ProjectId, ct); }
            catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }

            var id = launcher.Start(req.Flow, project.Name, project.Path, req.Prompt);
            return id is null
                ? Results.NotFound(new { error = $"Unknown flow '{req.Flow}'." })
                : Results.Ok(new StartRunResponse(id.Value));
        });
}
