namespace ABox.Tests.Harness;

// The repo layout on disk that the harness's own tests read: the repo root (located by the ABox.slnx marker),
// the central test tree under tests/Tests/, and the feature roots (src/, tools/) where a feature's co-located
// Tests/ folder may live. Source-tree queries (src/ projects, build output) live with the Structure type, which
// owns source placement; this owns the test system's layout. Root is the shared locator both sides build on.
// Throws on a missing root/tree so a broken scan can't go vacuously green.
public static class RepoTree
{
    private const string Marker = "ABox.slnx";
    private const string TestsFolder = "Tests";

    // The build-output directory names, owned once: the only legal artifacts home is the repo-root /artifacts,
    // so any folder by these names under src/ or tests/ is stray output. Both trees (here + Structure's
    // SourceTree) read this one fact.
    public static readonly string[] BuildOutputDirs = { "bin", "obj", "artifacts" };

    // The roots a feature's co-located Tests/ folder may sit under — in-solution features (src/) and standalone
    // tools (tools/). A Rulebook is central (tests/Tests) or owned by a feature here; nowhere else.
    public static readonly string[] FeatureRoots = { "src", "tools" };

    public static readonly string Root = LocateRoot();
    public static readonly string TestsRoot = RequireDir("the central test tree", "tests", "Tests");

    public static IReadOnlyList<string> TestTypeFolders() =>
        Directory.EnumerateDirectories(TestsRoot)
            .Select(Path.GetFileName)
            .Where(name => name is not null && !BuildOutputDirs.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    // Every Rulebook the format guard must keep well-formed, across both homes a Rulebook may have: the central
    // tree (each type under tests/Tests/) and every feature's co-located Tests/<Type>/Rulebook under src/ or
    // tools/. Format applies uniformly, regardless of which assembly owns the type. The feature arm returns the
    // live co-located suites (PLANS/test-colocation.md); the same scan covered the central-only set before the
    // migration and needs no re-wiring as features are added.
    public static IReadOnlyList<string> RulebookFolders() =>
        CentralRulebooks()
            .Concat(FeatureRulebooks())
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();

    private static IEnumerable<string> CentralRulebooks() =>
        Directory.EnumerateDirectories(TestsRoot)
            .Select(t => Path.Combine(t, "Rulebook"))
            .Where(Directory.Exists);

    private static IEnumerable<string> FeatureRulebooks() =>
        FeatureRoots
            .Select(r => Path.Combine(Root, r))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateDirectories(root, "Rulebook", SearchOption.AllDirectories))
            .Where(IsUnderFeatureTests);

    // Every co-located feature's Tests/ root on disk: the `Tests` folder under a src|tools feature that holds at
    // least one <Type>/Rulebook. The coverage guard cross-checks these against the built ABox.<Owner>.Tests
    // assemblies, so a Tests/ folder that ships tests but no assembly (untested feature slipping the net) is
    // caught. Disk-only, independent of what compiled — the same fail-loud surface RulebookFolders reads.
    public static IReadOnlyList<string> FeatureTestRoots() =>
        FeatureRoots
            .Select(r => Path.Combine(Root, r))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateDirectories(root, TestsFolder, SearchOption.AllDirectories))
            .Where(d => !IsBuildOutputPath(d))
            .Where(HasRulebookSubfolder)
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();

    private static bool HasRulebookSubfolder(string testsDir) =>
        Directory.EnumerateDirectories(testsDir)
            .Any(t => Directory.Exists(Path.Combine(t, "Rulebook")));

    private static bool IsBuildOutputPath(string dir) =>
        Path.GetRelativePath(Root, dir).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(s => BuildOutputDirs.Contains(s, StringComparer.OrdinalIgnoreCase));

    // A feature Rulebook sits inside a co-located Tests/ folder and never inside build output. Both checks read
    // the path's segments, so a Rulebook buried in src/.../Tests/Unit/Rulebook qualifies but one under bin/ does
    // not — the scan can't be fooled into validating a stale copy under artifacts.
    private static bool IsUnderFeatureTests(string dir)
    {
        var segments = Path.GetRelativePath(Root, dir).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains(TestsFolder, StringComparer.Ordinal)
            && !segments.Any(s => BuildOutputDirs.Contains(s, StringComparer.OrdinalIgnoreCase));
    }

    private static string LocateRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, Marker)))
                return dir.FullName;

        throw new InvalidOperationException(
            $"Could not locate the repo root: no '{Marker}' found walking up from '{AppContext.BaseDirectory}'. " +
            "The harness's own structure guards would be vacuously green — fix the marker or the locator.");
    }

    private static string RequireDir(string what, params string[] segments)
    {
        var path = Path.Combine(new[] { Root }.Concat(segments).ToArray());
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(
                $"No '{string.Join('/', segments)}/' under '{Root}' ({what}). The harness's own guards would be " +
                "vacuously green — fix the locator or the layout.");
        return path;
    }
}
