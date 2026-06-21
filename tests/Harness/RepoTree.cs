namespace ABox.Tests.Harness;

// The repo layout on disk that the Meta self-suite reads: the repo root (located by the ABox.slnx marker), the
// product test tree under tests/Tests/, and Meta's own home under tests/Meta/. Source-tree queries (src/
// projects, build output) live with the Structure type, which owns source placement; this owns the test
// system's layout. Root is the shared locator both sides build on. Throws on a missing root/tree so a broken
// scan can't go vacuously green.
public static class RepoTree
{
    private const string Marker = "ABox.slnx";
    public const string RulebookDir = "Rulebook";

    // The build-output directory names, owned once: the only legal artifacts home is the repo-root /artifacts,
    // so any folder by these names under src/ or tests/ is stray output. Both trees (here + Structure's
    // SourceTree) read this one fact.
    public static readonly string[] BuildOutputDirs = { "bin", "obj", "artifacts" };

    public static readonly string Root = LocateRoot();
    public static readonly string TestsRoot = RequireDir("the product test tree", "tests", "Tests");
    public static readonly string MetaRoot = RequireDir("the Meta self-suite", "tests", "Meta");

    public static IReadOnlyList<string> TestTypeFolders() =>
        Directory.EnumerateDirectories(TestsRoot)
            .Select(Path.GetFileName)
            .Where(name => name is not null && !BuildOutputDirs.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    // Every Rulebook the format guard must keep well-formed: each product type's under tests/Tests/, plus
    // Meta's own under tests/Meta/. Format applies uniformly, regardless of which assembly owns the type.
    public static IReadOnlyList<string> RulebookFolders() =>
        Directory.EnumerateDirectories(TestsRoot)
            .Select(t => Path.Combine(t, RulebookDir))
            .Append(Path.Combine(MetaRoot, RulebookDir))
            .Where(Directory.Exists)
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();

    // The one owner of where a type's rules.md lives — a product type under tests/Tests/<type>/, the self-suite
    // under tests/Meta/. ParityGuard (product) and the Meta self-parity both derive their path from here.
    public static string ProductRulesPath(string type) => Path.Combine(TestsRoot, type, RulebookDir, "rules.md");
    public static string MetaRulesPath() => Path.Combine(MetaRoot, RulebookDir, "rules.md");

    private static string LocateRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, Marker)))
                return dir.FullName;

        throw new InvalidOperationException(
            $"Could not locate the repo root: no '{Marker}' found walking up from '{AppContext.BaseDirectory}'. " +
            "The Meta structure guards would be vacuously green — fix the marker or the locator.");
    }

    private static string RequireDir(string what, params string[] segments)
    {
        var path = Path.Combine(new[] { Root }.Concat(segments).ToArray());
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(
                $"No '{string.Join('/', segments)}/' under '{Root}' ({what}). The Meta guards would be " +
                "vacuously green — fix the locator or the layout.");
        return path;
    }
}
