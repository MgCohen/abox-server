using FastEndpoints;
using ABox.Domain.Projects;
using ABox.Features.Projects.Contracts;

namespace ABox.Features.Projects.List;

public sealed class ListProjectsEndpoint(IProjects store) : EndpointWithoutRequest<IReadOnlyList<ProjectDto>>
{
    public override void Configure()
    {
        Get("/projects");
        AllowAnonymous();
    }

    public override Task HandleAsync(CancellationToken ct) =>
        Send.OkAsync([.. store.List().Select(p => new ProjectDto(p.Id, p.Name))], ct);
}
