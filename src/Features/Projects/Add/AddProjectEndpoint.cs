using FastEndpoints;
using ABox.Domain.Projects;
using ABox.Features.Projects.Contracts;
using ABox.Features.Projects.Get;

namespace ABox.Features.Projects.Add;

public sealed class AddProjectEndpoint(IProjectRepository projects) : Endpoint<CreateProjectRequest, ProjectDto>
{
    public override void Configure()
    {
        Post("/projects");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CreateProjectRequest req, CancellationToken ct)
    {
        var name = req.Name?.Trim() ?? string.Empty;
        var path = req.Path?.Trim() ?? string.Empty;

        // Provisional: keep blank name/path a clean 400 here, not a 500 from the model's throw; moves to a Step (ADR 0009).
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

        if (await projects.GetByName(name, ct) is not null)
        {
            AddError(r => r.Name, "A project with that name already exists.");
            await Send.ErrorsAsync(409, ct);
            return;
        }

        var project = Project.Create(name, path);
        await projects.Add(project, ct);
        await Send.CreatedAtAsync<GetProjectEndpoint>(
            new { id = project.Id }, new ProjectDto(project.Id, project.Name, project.Path), cancellation: ct);
    }
}
