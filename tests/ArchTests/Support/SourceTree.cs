namespace RemoteAgents.Tests.ArchTests;

// The physical project layout on disk — the placement guard's input, independent of what compiled.
// ArchUnitNET only sees loaded assemblies; this sees every project folder under src/ the moment it
// exists (Web/Morph/new folders included), so project placement can't be dodged by simply not building.
// (Namespace-matches-folder is enforced separately by IDE0130 at compile time — see /.editorconfig.)
// The locator throws on a missing root or zero projects so a broken scan can't go vacuously green.
internal static class SourceTree
{
    private const string Marker = "RemoteAgents.slnx";
    private static readonly string[] Ignored = { "bin", "obj", "artifacts" };
    private static readonly char[] Separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    public static readonly string SrcRoot = LocateSrc();

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

    private static IEnumerable<string> Projects() =>
        Directory.EnumerateFiles(SrcRoot, "*.csproj", SearchOption.AllDirectories).Where(NotIgnored);

    private static bool NotIgnored(string path) =>
        !Path.GetRelativePath(SrcRoot, path).Split(Separators)
            .Any(seg => Ignored.Contains(seg, StringComparer.OrdinalIgnoreCase));

    private static string TopSegment(string path) =>
        Path.GetRelativePath(SrcRoot, path).Split(Separators)[0];

    private static string LocateSrc()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, Marker)))
                return Directory.Exists(Path.Combine(dir.FullName, "src"))
                    ? Path.Combine(dir.FullName, "src")
                    : throw new DirectoryNotFoundException(
                        $"Found '{Marker}' at '{dir.FullName}' but no 'src/' beside it — the structure guard can't run.");

        throw new InvalidOperationException(
            $"Could not locate the repo root: no '{Marker}' found walking up from '{AppContext.BaseDirectory}'. " +
            "The source-tree structure guard would be vacuously green — fix the marker or the locator.");
    }
}
