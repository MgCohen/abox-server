using FastEndpoints;
using ABox.Domain.Projects;
using ABox.Features.Projects.Contracts;
using ABox.Infrastructure.Storage;

namespace ABox.Features.Projects.Get;

public sealed class GetProjectEndpoint(IRepository<Project> store) : Endpoint<GetProjectRequest, ProjectDto>
{
    public override void Configure()
    {
        Get("/projects/{id}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetProjectRequest req, CancellationToken ct)
    {
        if (await store.GetById(req.Id, ct) is not { } project)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(new ProjectDto(project.Id, project.Name, project.Path), ct);
    }
}
