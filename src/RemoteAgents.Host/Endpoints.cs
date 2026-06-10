using Microsoft.AspNetCore.Http;
using RemoteAgents.Contracts;
using RemoteAgents.Domain.Flow;
using RemoteAgents.Infrastructure.Projects;

namespace RemoteAgents.Host;

internal static class Endpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapGet("/projects", (IProjectRegistry projects) =>
            projects.List().Select(p => new ProjectInfo(p.Name, p.Path)));

        app.MapGet("/catalog", (FlowCatalog catalog) =>
            catalog.All().Select(d => new FlowInfo(d.Config.Name, d.Config.Description)));

        var flows = app.MapGroup("/flows");

        flows.MapPost("/", (StartRunRequest req, FlowLauncher launcher, IProjectRegistry projects) =>
        {
            string projectDir;
            try { projectDir = projects.Resolve(req.Project); }
            catch (Exception ex) { return Results.BadRequest(Error(ex.Message)); }

            var id = launcher.Start(req.Flow, req.Project, projectDir, req.Prompt);
            return id is null
                ? Results.NotFound(Error($"Unknown flow '{req.Flow}'."))
                : Results.Ok(new StartRunResponse(id.Value));
        });

        flows.MapGet("/", (FlowRegistry runs) => runs.List());

        flows.MapGet("/{id:guid}", (Guid id, FlowRegistry runs, HttpContext http) =>
        {
            var snap = runs.Get(id);
            if (snap is null) return Results.NotFound();

            var etag = $"\"{snap.Version}\"";
            if ((string?)http.Request.Headers.IfNoneMatch == etag)
                return Results.StatusCode(StatusCodes.Status304NotModified);

            http.Response.Headers.ETag = etag;
            return Results.Ok(snap);
        });

        flows.MapGet("/{id:guid}/events", (Guid id, FlowRegistry runs, HttpContext http, CancellationToken ct) =>
            Sse.Stream(http, runs.Changes(id, ct), ct));

        flows.MapPost("/{id:guid}/cancel", (Guid id, FlowRegistry runs) =>
            runs.Cancel(id) ? Results.Accepted() : Results.NotFound());
    }

    private static object Error(string message) => new { error = message };
}
