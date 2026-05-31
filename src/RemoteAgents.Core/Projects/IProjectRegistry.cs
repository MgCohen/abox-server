namespace RemoteAgents.Core.Projects;

/// <summary>A registered project: short name + its absolute directory.</summary>
public readonly record struct ProjectEntry(string Name, string Path);

/// <summary>
/// Resolves short project names (used on the wire) to absolute directories,
/// from the <c>projects.json</c> map at the orchestrator root.
/// </summary>
public interface IProjectRegistry
{
    /// <summary>
    /// Resolve a registered name — or an absolute path passed directly — to an
    /// existing absolute directory. Throws if the name is unknown or the
    /// directory does not exist. Use when actually launching against a project.
    /// </summary>
    string Resolve(string nameOrPath);

    /// <summary>
    /// All registered projects as name + absolute (configured) path. Does NOT
    /// check existence, so listing never throws on a stale entry.
    /// </summary>
    IReadOnlyList<ProjectEntry> List();

    /// <summary>Absolute path of the backing <c>projects.json</c>.</summary>
    string ProjectsFilePath { get; }
}
