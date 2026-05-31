using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RemoteAgents.Core.Projects;

namespace RemoteAgents.Hosting;

/// <summary>The orchestrator's HTTP endpoints.</summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// Map the orchestrator endpoints. At L1: liveness + the project list.
    /// Later layers add catalog, flows, SSE, cancel, answer.
    /// </summary>
    public static IEndpointRouteBuilder MapRemoteAgents(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapGet("/projects", (IProjectRegistry projects) =>
            Results.Ok(projects.List().Select(p => new { name = p.Name, path = p.Path })));

        return app;
    }
}
