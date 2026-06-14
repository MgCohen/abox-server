using FastEndpoints;
using ABox.Domain.Projects;
using ABox.Features.Projects.Contracts;
using ABox.Infrastructure.Storage;

namespace ABox.Features.Projects.Delete;

public sealed class DeleteProjectEndpoint(IRepository<Project> store) : Endpoint<GetProjectRequest>
{
    public override void Configure()
    {
        Delete("/projects/{id}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetProjectRequest req, CancellationToken ct)
    {
        if (await store.GetById(req.Id, ct) is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await store.Remove(req.Id, ct);
        await Send.NoContentAsync(ct);
    }
}
