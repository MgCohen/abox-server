using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ABox.Domain.Projects;
using ABox.Features.Projects.Contracts;

namespace ABox.Features.Projects.List;

public static class ListProjectsEndpoint
{
    public static void Map(IEndpointRouteBuilder projects) =>
        projects.MapGet("/", (IProjects store) =>
            Results.Ok(store.List().Select(p => new ProjectDto(p.Id, p.Name))));
}
