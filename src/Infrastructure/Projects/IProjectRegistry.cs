namespace RemoteAgents.Infrastructure.Projects;

public readonly record struct ProjectEntry(string Name, string Path);

public interface IProjectRegistry
{
    string Resolve(string nameOrPath);

    IReadOnlyList<ProjectEntry> List();

    string ProjectsFilePath { get; }
}
