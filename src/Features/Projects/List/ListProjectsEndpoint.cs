using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ABox.Domain.Projects;
using ABox.Features.Projects.Contracts;
using ABox.Infrastructure.Storage;

namespace ABox.Features.Projects.List;

public static class ListProjectsEndpoint
{
    public static void Map(IEndpointRouteBuilder projects) =>
        projects.MapGet("/", async (IRepository<Project> store, CancellationToken ct) =>
            Results.Ok((await store.GetAll(ct)).Select(p => new ProjectDto(p.Id, p.Name))));
}
