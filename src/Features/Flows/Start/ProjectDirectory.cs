using ABox.Domain.Projects;

namespace ABox.Features.Flows.Start;

public sealed class ProjectDirectory(IProjectRepository projects)
{
    public async Task<string> Resolve(string nameOrPath, CancellationToken ct = default)
    {
        if (Path.IsPathRooted(nameOrPath) && Directory.Exists(nameOrPath))
            return Path.GetFullPath(nameOrPath);

        if (await projects.GetByName(nameOrPath, ct) is not { } project)
            throw new InvalidOperationException(
                $"Unknown project \"{nameOrPath}\". Create it via POST /projects, or pass an absolute path.");

        if (!Directory.Exists(project.Path))
            throw new InvalidOperationException(
                $"Project \"{nameOrPath}\" resolves to {project.Path} but it doesn't exist.");

        return project.Path;
    }
}
