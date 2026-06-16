using FastEndpoints;
using ABox.Domain.Projects;
using ABox.Features.Projects.Contracts;

namespace ABox.Features.Projects.Get;

internal sealed class GetProjectEndpoint(IProjectRepository projects) : Endpoint<ProjectByIdRequest, ProjectDto>
{
    public override void Configure()
    {
        Get("/projects/{id}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(ProjectByIdRequest req, CancellationToken ct)
    {
        if (await projects.GetById(req.Id, ct) is not { } project)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(new ProjectDto(project.Id, project.Name, project.Path), ct);
    }
}
