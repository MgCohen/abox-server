using System.Text.Json;
using System.Text.Json.Serialization;
using RemoteAgents.Paths;

namespace RemoteAgents.Projects;

/// <inheritdoc />
public sealed class ProjectRegistry : IProjectRegistry
{
    private readonly Lazy<IReadOnlyDictionary<string, string>> _projects;

    public ProjectRegistry(IOrchestratorPaths paths)
    {
        ProjectsFilePath = paths.ProjectsFile;
        _projects = new Lazy<IReadOnlyDictionary<string, string>>(Load);
    }

    public string ProjectsFilePath { get; }

    public string Resolve(string nameOrPath)
    {
        // Allow passing an absolute path directly.
        if (Path.IsPathRooted(nameOrPath) && Directory.Exists(nameOrPath))
            return Path.GetFullPath(nameOrPath);

        var projects = _projects.Value;
        if (!projects.TryGetValue(nameOrPath, out var configured))
        {
            throw new InvalidOperationException(
                $"Unknown project \"{nameOrPath}\". Known: {string.Join(", ", projects.Keys)}. " +
                $"Edit {ProjectsFilePath} to add more, or pass an absolute path.");
        }

        var abs = Path.GetFullPath(configured);
        if (!Directory.Exists(abs))
            throw new InvalidOperationException($"Project \"{nameOrPath}\" resolves to {abs} but it doesn't exist.");
        return abs;
    }

    public IReadOnlyList<ProjectEntry> List() =>
        [.. _projects.Value.Select(kv => new ProjectEntry(kv.Key, Path.GetFullPath(kv.Value)))];

    private IReadOnlyDictionary<string, string> Load()
    {
        if (!File.Exists(ProjectsFilePath))
            throw new FileNotFoundException(
                $"projects.json not found at {ProjectsFilePath} (expected at the orchestrator root).");

        var raw = File.ReadAllText(ProjectsFilePath);
        return JsonSerializer.Deserialize(raw, ProjectsJsonContext.Default.DictionaryStringString)
            ?? throw new InvalidDataException($"projects.json at {ProjectsFilePath} did not parse as a string map.");
    }
}

[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class ProjectsJsonContext : JsonSerializerContext { }
