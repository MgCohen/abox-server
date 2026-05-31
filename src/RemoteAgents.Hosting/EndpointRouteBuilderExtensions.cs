using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using RemoteAgents.Contracts;
using RemoteAgents.Core.Projects;
using RemoteAgents.Flows;

namespace RemoteAgents.Hosting;

/// <summary>The orchestrator's HTTP endpoints.</summary>
public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapRemoteAgents(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapGet("/projects", (IProjectRegistry projects) =>
            projects.List().Select(p => new ProjectInfo(p.Name, p.Path)));

        app.MapGet("/catalog", (FlowCatalog catalog) =>
            catalog.All().Select(e => new FlowInfo(e.Name, e.Description)));

        app.MapPost("/flows", (StartRunRequest req, FlowCatalog catalog, FlowRegistry runs,
                               IProjectRegistry projects, IServiceProvider sp) =>
        {
            var entry = catalog.Resolve(req.Flow);
            if (entry is null)
                return Results.NotFound(new { error = $"Unknown flow '{req.Flow}'." });

            string projectDir;
            try { projectDir = projects.Resolve(req.Project); }
            catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }

            var flow = (Flow)sp.GetRequiredService(entry.FlowType);
            var id = runs.Start(flow, req.Project, projectDir, req.Prompt, req.Args ?? []);
            return Results.Ok(new StartRunResponse(id));
        });

        app.MapGet("/flows", (FlowRegistry runs) => runs.List());

        app.MapGet("/flows/{id:guid}", (Guid id, FlowRegistry runs, HttpContext http) =>
        {
            var snap = runs.Get(id);
            if (snap is null) return Results.NotFound();

            var etag = $"\"{snap.Version}\"";
            if ((string?)http.Request.Headers.IfNoneMatch == etag)
                return Results.StatusCode(StatusCodes.Status304NotModified);

            http.Response.Headers.ETag = etag;
            return Results.Ok(snap);
        });

        app.MapGet("/flows/{id:guid}/events", async (Guid id, FlowRegistry runs, HttpContext http, CancellationToken ct) =>
        {
            http.Response.Headers.ContentType = "text/event-stream";
            http.Response.Headers.CacheControl = "no-cache";

            var flow = runs.GetLive(id);
            if (flow is null)
            {
                // Finished or unknown: one static snapshot (if any), no stream.
                var snap = runs.Get(id);
                if (snap is not null) await Sse.Write(http, snap, ct);
                return;
            }

            await foreach (var snap in flow.Changes(ct))
                await Sse.Write(http, snap, ct);
        });

        app.MapPost("/flows/{id:guid}/cancel", (Guid id, FlowRegistry runs) =>
            runs.Cancel(id) ? Results.Accepted() : Results.NotFound());

        return app;
    }
}

/// <summary>Minimal Server-Sent-Events writer.</summary>
internal static class Sse
{
    public static async Task Write(HttpContext http, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, WireJson.Options);
        await http.Response.WriteAsync($"data: {json}\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }
}
