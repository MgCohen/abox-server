using FastEndpoints;
using ABox.Domain.Projects;
using ABox.Features.Projects.Api;

namespace ABox.Features.Projects.List;

internal sealed class ListProjectsEndpoint(IProjectRepository projects) : EndpointWithoutRequest<IReadOnlyList<ProjectDto>>
{
    public override void Configure()
    {
        Get("/projects");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var all = await projects.GetAll(ct);
        await Send.OkAsync([.. all.Select(p => new ProjectDto(p.Id, p.Name, p.Path))], ct);
    }
}
