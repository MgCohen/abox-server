namespace ABox.Tests.Structure.Support;

// The physical project layout on disk — the placement guards' input, independent of what compiled.
// ArchUnitNET only sees loaded assemblies; this sees every project folder under src/ the moment it
// exists (new folders included), so project placement can't be dodged by simply not building,
// and stray build output (gitignored, invisible to the reference graph) is still caught on disk.
// (Namespace-matches-folder is enforced separately by IDE0130 at compile time — see /.editorconfig.)
// The locator throws on a missing root or src so a broken scan can't go vacuously green.
internal static class SourceTree
{
    private const string Marker = "ABox.slnx";
    private static readonly string[] Ignored = { "bin", "obj", "artifacts" };
    private static readonly char[] Separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    public static readonly string Root = LocateRoot();
    public static readonly string SrcRoot = RequireDir("src");

    public static IReadOnlyList<string> ProjectTopSegments()
    {
        var segments = Projects().Select(TopSegment).Distinct().ToList();
        if (segments.Count == 0)
            throw new InvalidOperationException(
                $"No *.csproj found under '{SrcRoot}'. The source-tree guard would be vacuously green — " +
                "the locator or layout is broken.");
        return segments;
    }

    public static bool HasTopSegment(string segment) =>
        Projects().Any(p => string.Equals(TopSegment(p), segment, StringComparison.Ordinal));

    public static IReadOnlyList<string> TestTypeFolders()
    {
        var testsRoot = Path.Combine(Root, "tests", "Tests");
        if (!Directory.Exists(testsRoot))
            throw new DirectoryNotFoundException(
                $"No 'tests/Tests/' under '{Root}'. The test-taxonomy guard would be vacuously green — " +
                "fix the locator or the layout.");

        return Directory.EnumerateDirectories(testsRoot)
            .Select(Path.GetFileName)
            .Where(name => name is not null && !Ignored.Contains(name, StringComparer.OrdinalIgnoreCase))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    // The top-most bin/obj/artifacts folders found under src/ or tests/ — Rule 13. The only legal
    // artifacts home is the repo-root /artifacts; any here means a project escaped the root props.
    public static IReadOnlyList<string> StrayBuildOutput() =>
        SourceRoots()
            .SelectMany(root => Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
            .Select(d => Path.GetRelativePath(Root, d))
            .Where(IsOutputRoot)
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();

    private static IEnumerable<string> SourceRoots()
    {
        yield return SrcRoot;
        var tests = Path.Combine(Root, "tests");
        if (Directory.Exists(tests))
            yield return tests;
    }

    // An output ROOT: its own name is bin/obj/artifacts and no ancestor is — so we report `src/Features/
    // artifacts` once, not every bin/obj nested beneath it.
    private static bool IsOutputRoot(string relativePath)
    {
        var segments = relativePath.Split(Separators);
        return Ignored.Contains(segments[^1], StringComparer.OrdinalIgnoreCase)
            && !segments[..^1].Any(s => Ignored.Contains(s, StringComparer.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> Projects() =>
        Directory.EnumerateFiles(SrcRoot, "*.csproj", SearchOption.AllDirectories).Where(NotIgnored);

    private static bool NotIgnored(string path) =>
        !Path.GetRelativePath(SrcRoot, path).Split(Separators)
            .Any(seg => Ignored.Contains(seg, StringComparer.OrdinalIgnoreCase));

    private static string TopSegment(string path) =>
        Path.GetRelativePath(SrcRoot, path).Split(Separators)[0];

    private static string LocateRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, Marker)))
                return dir.FullName;

        throw new InvalidOperationException(
            $"Could not locate the repo root: no '{Marker}' found walking up from '{AppContext.BaseDirectory}'. " +
            "The source-tree structure guard would be vacuously green — fix the marker or the locator.");
    }

    private static string RequireDir(string name) =>
        Directory.Exists(Path.Combine(Root, name))
            ? Path.Combine(Root, name)
            : throw new DirectoryNotFoundException(
                $"Found '{Marker}' at '{Root}' but no '{name}/' beside it — the structure guard can't run.");
}
