using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteAgents.Primitives;

[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class ProjectsJsonContext : JsonSerializerContext { }

// Resolve a short project name (passed on the CLI) to an absolute directory.
// Lookup table lives in <repo>/projects.json — discovered by walking up from
// the current directory until we find it.
public static class ProjectRegistry
{
    private static readonly object _lock = new();
    private static Dictionary<string, string>? _cache;
    private static string? _cachedPath;

    public static string Resolve(string name)
    {
        // Allow passing an absolute path directly.
        if (Path.IsPathRooted(name) && Directory.Exists(name)) return name;

        var projects = Load();
        if (!projects.TryGetValue(name, out var hit))
        {
            var known = string.Join(", ", projects.Keys);
            throw new InvalidOperationException(
                $"Unknown project \"{name}\". Known: {known}\n" +
                "Edit projects.json to add more, or pass an absolute path.");
        }
        var abs = Path.GetFullPath(hit);
        if (!Directory.Exists(abs))
            throw new InvalidOperationException($"Project \"{name}\" resolves to {abs} but it doesn't exist.");
        return abs;
    }

    public static IReadOnlyList<string> List() => [.. Load().Keys];

    public static string ProjectsFilePath() { _ = Load(); return _cachedPath!; }

    private static Dictionary<string, string> Load()
    {
        lock (_lock)
        {
            if (_cache is not null) return _cache;

            var path = FindProjectsJson()
                ?? throw new FileNotFoundException(
                    "projects.json not found. Walked up from current directory looking for it; " +
                    "expected at the repo root (sibling of remote-agents-dotnet/).");

            var raw = File.ReadAllText(path);
            var parsed = JsonSerializer.Deserialize(raw, ProjectsJsonContext.Default.DictionaryStringString)
                ?? throw new InvalidDataException($"projects.json at {path} did not parse as Dictionary<string,string>.");
            _cache = parsed;
            _cachedPath = path;
            return _cache;
        }
    }

    private static string? FindProjectsJson()
    {
        var root = RepoRoot.Find("projects.json");
        return root is null ? null : Path.Combine(root, "projects.json");
    }

    // Test hook: reset the cache so a test can point at a different file.
    internal static void ResetCacheForTesting() { lock (_lock) { _cache = null; _cachedPath = null; } }
}
