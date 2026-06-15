using ABox.Domain.Projects;

namespace ABox.Features.Flows.Start;

public sealed class ProjectResolver(IProjectRepository projects)
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
