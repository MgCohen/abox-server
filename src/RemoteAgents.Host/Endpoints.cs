using Microsoft.AspNetCore.Http;
using RemoteAgents.Contracts;
using RemoteAgents.Flows;
using RemoteAgents.Projects;

namespace RemoteAgents.Host;

/// <summary>
/// The orchestrator's HTTP surface: health, projects, catalog, and the <c>/flows</c>
/// group (start / list / snapshot+ETag / SSE / cancel). Companion to <see cref="Composition"/>.
/// </summary>
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

        flows.MapPost("/", (StartRunRequest req, FlowCatalog catalog, IFlowFactory factory,
                            FlowRegistry runs, IProjectRegistry projects) =>
        {
            var def = catalog.Resolve(req.Flow);
            if (def is null)
                return Results.NotFound(new { error = $"Unknown flow '{req.Flow}'." });

            string projectDir;
            try { projectDir = projects.Resolve(req.Project); }
            catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }

            var flow = factory.Create(def);
            var id = runs.Start(flow, def.Config, req.Project, projectDir, req.Prompt, req.Args ?? []);
            return Results.Ok(new StartRunResponse(id));
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
}
