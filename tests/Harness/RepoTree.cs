namespace ABox.Tests.Harness;

// The repo layout on disk that the Meta self-suite reads: the repo root (located by the ABox.slnx marker), the
// product test tree under tests/Tests/, and Meta's own home under tests/Meta/. Source-tree queries (src/
// projects, build output) live with the Structure type, which owns source placement; this owns the test
// system's layout. Root is the shared locator both sides build on. Throws on a missing root/tree so a broken
// scan can't go vacuously green.
public static class RepoTree
{
    private const string Marker = "ABox.slnx";

    // The build-output directory names, owned once: the only legal artifacts home is the repo-root /artifacts,
    // so any folder by these names under src/ or tests/ is stray output. Both trees (here + Structure's
    // SourceTree) read this one fact.
    public static readonly string[] BuildOutputDirs = { "bin", "obj", "artifacts" };

    public static readonly string Root = LocateRoot();
    public static readonly string TestsRoot = RequireDir("the product test tree", "tests", "Tests");
    public static readonly string MetaRoot = RequireDir("the Meta self-suite", "tests", "Meta");

    // The artifact registry root. Not RequireDir: it may not exist yet (no artifacts registered), and the
    // floor guard reads it leniently — an absent registry is "no artifacts", not a broken scan.
    public static readonly string RegistryRoot = Path.Combine(Root, "governance", "registry");

    // The Test artifact's sub-type definitions (template.md + rules.md per type), relocated here from
    // tests/Tests/<Type>/Rulebook/ and tests/Meta/Rulebook/. Required — the definitions must exist.
    public static readonly string TestArtifactRoot = RequireDir("the Test artifact definitions", "governance", "registry", "Test");

    public static IReadOnlyList<string> TestTypeFolders() =>
        Directory.EnumerateDirectories(TestsRoot)
            .Select(Path.GetFileName)
            .Where(name => name is not null && !BuildOutputDirs.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    // Every Rulebook the format guard must keep well-formed: each Test sub-type's definition under
    // governance/registry/Test/<Type>/ (each holds a template.md). Relocated from tests/; parity bridges back.
    public static IReadOnlyList<string> RulebookFolders() =>
        Directory.EnumerateDirectories(TestArtifactRoot)
            .Where(d => File.Exists(Path.Combine(d, "template.md")))
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();

    // The one owner of where a Test sub-type's rules.md lives, under governance/registry/Test/<Type>/.
    // ParityGuard (product types) and the Meta self-parity both derive their path from here.
    public static string ProductRulesPath(string type) => Path.Combine(TestArtifactRoot, type, "rules.md");
    public static string MetaRulesPath() => Path.Combine(TestArtifactRoot, "Meta", "rules.md");

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
