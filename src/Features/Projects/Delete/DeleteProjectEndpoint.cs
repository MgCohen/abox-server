using FastEndpoints;
using ABox.Domain.Projects;
using ABox.Features.Projects.Api;

namespace ABox.Features.Projects.Delete;

internal sealed class DeleteProjectEndpoint(IProjectRepository projects) : Endpoint<ProjectByIdRequest>
{
    public override void Configure()
    {
        Delete("/projects/{id}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(ProjectByIdRequest req, CancellationToken ct)
    {
        if (await projects.GetById(req.Id, ct) is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await projects.Remove(req.Id, ct);
        await Send.NoContentAsync(ct);
    }
}
