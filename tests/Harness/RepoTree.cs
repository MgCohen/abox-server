namespace ABox.Tests.Harness;

// The repo layout on disk that the Meta (test-system) guards read: the repo root, located by the ABox.slnx
// marker, and the test tree under tests/Tests/. Source-tree queries (src/ projects, build output) live with
// the Structure type, which owns source placement; this owns the test system's own layout. Root is the shared
// locator both sides build on. Throws on a missing root/tree so a broken scan can't go vacuously green.
public static class RepoTree
{
    private const string Marker = "ABox.slnx";
    private static readonly string[] Ignored = { "bin", "obj", "artifacts" };

    public static readonly string Root = LocateRoot();
    public static readonly string TestsRoot = RequireTestsRoot();

    public static IReadOnlyList<string> TestTypeFolders() =>
        Directory.EnumerateDirectories(TestsRoot)
            .Select(Path.GetFileName)
            .Where(name => name is not null && !Ignored.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    public static IReadOnlyList<string> RulebookFolders() =>
        Directory.EnumerateDirectories(TestsRoot)
            .Select(t => Path.Combine(t, "Rulebook"))
            .Where(Directory.Exists)
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();

    private static string LocateRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, Marker)))
                return dir.FullName;

        throw new InvalidOperationException(
            $"Could not locate the repo root: no '{Marker}' found walking up from '{AppContext.BaseDirectory}'. " +
            "The Meta structure guards would be vacuously green — fix the marker or the locator.");
    }

    private static string RequireTestsRoot()
    {
        var path = Path.Combine(Root, "tests", "Tests");
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(
                $"No 'tests/Tests/' under '{Root}'. The Meta structure guards would be vacuously green — " +
                "fix the locator or the layout.");
        return path;
    }
}
