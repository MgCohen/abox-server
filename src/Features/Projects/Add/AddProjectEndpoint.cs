using FastEndpoints;
using ABox.Domain.Projects;
using ABox.Features.Projects.Contracts;
using ABox.Features.Projects.Get;
using ABox.Infrastructure.Storage;

namespace ABox.Features.Projects.Add;

public sealed class AddProjectEndpoint(IRepository<Project> store) : Endpoint<CreateProjectRequest, ProjectDto>
{
    public override void Configure()
    {
        Post("/projects");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CreateProjectRequest req, CancellationToken ct)
    {
        var name = req.Name?.Trim() ?? string.Empty;

        // Provisional: keep a blank name a clean 400 here, not a 500 from the model's throw; moves to a Step (ADR 0009).
        if (name.Length == 0)
        {
            AddError(r => r.Name, "Project name is required.");
            await Send.ErrorsAsync(400, ct);
            return;
        }

        if ((await store.GetAll(ct)).Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            AddError(r => r.Name, "A project with that name already exists.");
            await Send.ErrorsAsync(409, ct);
            return;
        }

        var project = Project.Create(name);
        await store.Add(project, ct);
        await Send.CreatedAtAsync<GetProjectEndpoint>(
            new { id = project.Id }, new ProjectDto(project.Id, project.Name), cancellation: ct);
    }
}
