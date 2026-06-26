using FastEndpoints;
using ABox.Domain.Projects;
using ABox.Features.Projects.Api;

namespace ABox.Features.Projects.Update;

internal sealed class UpdateProjectEndpoint(IProjectRepository projects) : Endpoint<UpdateProjectRequest, ProjectDto>
{
    public override void Configure()
    {
        Put("/projects/{id}");
        AllowAnonymous();
    }

    public override async Task HandleAsync(UpdateProjectRequest req, CancellationToken ct)
    {
        var name = req.Name?.Trim() ?? string.Empty;
        var path = req.Path?.Trim() ?? string.Empty;

        // Provisional: keep blank name/path a clean 400, not a 500 from the model's throw (mirrors Add).
        if (name.Length == 0)
        {
            AddError(r => r.Name, "Project name is required.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        if (path.Length == 0)
        {
            AddError(r => r.Path, "Project path is required.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        if (await projects.GetById(req.Id, ct) is not { } existing)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (await projects.GetByName(name, ct) is { } clash && clash.Id != req.Id)
        {
            AddError(r => r.Name, "A project with that name already exists.");
            await Send.ErrorsAsync(409, ct);
            return;
        }

        var updated = existing.Rename(name).MoveTo(path);
        await projects.Update(updated, ct);
        await Send.OkAsync(new ProjectDto(updated.Id, updated.Name, updated.Path), ct);
    }
}
