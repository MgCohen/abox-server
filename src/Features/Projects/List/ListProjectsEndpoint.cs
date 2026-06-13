using FastEndpoints;
using ABox.Domain.Projects;
using ABox.Features.Projects.Contracts;
using ABox.Infrastructure.Storage;

namespace ABox.Features.Projects.List;

public sealed class ListProjectsEndpoint(IRepository<Project> store) : EndpointWithoutRequest<IReadOnlyList<ProjectDto>>
{
    public override void Configure()
    {
        Get("/projects");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var projects = await store.GetAll(ct);
        await Send.OkAsync([.. projects.Select(p => new ProjectDto(p.Id, p.Name))], ct);
    }
}
