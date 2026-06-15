using ABox.Domain.Projects;
using ABox.Infrastructure.Storage;

namespace ABox.Features.Flows.Start;

public sealed class ProjectResolver(IRepository<Project> projects)
{
    public async Task<Project> Resolve(Guid id, CancellationToken ct = default)
    {
        if (await projects.GetById(id, ct) is not { } project)
            throw new InvalidOperationException($"Unknown project '{id}'. Create it via POST /projects.");

        if (!Directory.Exists(project.Path))
            throw new InvalidOperationException(
                $"Project '{project.Name}' resolves to {project.Path} but it doesn't exist.");

        return project;
    }
}
