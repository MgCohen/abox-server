using System.Text.RegularExpressions;

namespace RemoteAgents.Tests.ArchTests;

// The physical source tree on disk — the placement/naming guard's input, independent of what compiled.
// ArchUnitNET only sees loaded assemblies; this sees every folder and .cs under src/ the moment it
// exists (Web/Morph/new folders included), so the structure rules can't be dodged by simply not
// building. Locator throws on a missing root or zero projects so a broken scan can't go vacuously green.
internal static class SourceTree
{
    private const string Marker = "RemoteAgents.slnx";
    private static readonly string[] Ignored = { "bin", "obj", "artifacts" };
    private static readonly char[] Separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    private static readonly Regex FileScopedNamespace =
        new(@"^\s*namespace\s+([A-Za-z0-9_.]+)\s*;", RegexOptions.Multiline);

    public static readonly string SrcRoot = LocateSrc();

    public static IReadOnlyList<string> ProjectTopSegments()
    {
        var segments = Enumerate("*.csproj").Select(TopSegment).Distinct().ToList();
        if (segments.Count == 0)
            throw new InvalidOperationException(
                $"No *.csproj found under '{SrcRoot}'. The source-tree guard would be vacuously green — " +
                "the locator or layout is broken.");
        return segments;
    }

    public static IReadOnlyList<SourceFile> SourceFiles() =>
        Enumerate("*.cs").Select(ToSourceFile).ToList();

    public static bool HasTopSegment(string segment) =>
        Enumerate("*.csproj").Any(p => string.Equals(TopSegment(p), segment, StringComparison.Ordinal));

    private static IEnumerable<string> Enumerate(string pattern) =>
        Directory.EnumerateFiles(SrcRoot, pattern, SearchOption.AllDirectories).Where(NotIgnored);

    private static bool NotIgnored(string path) =>
        !Path.GetRelativePath(SrcRoot, path).Split(Separators)
            .Any(seg => Ignored.Contains(seg, StringComparer.OrdinalIgnoreCase));

    private static string TopSegment(string path) =>
        Path.GetRelativePath(SrcRoot, path).Split(Separators)[0];

    private static SourceFile ToSourceFile(string path)
    {
        var relFolder = Path.GetRelativePath(SrcRoot, Path.GetDirectoryName(path)!);
        var match = FileScopedNamespace.Match(File.ReadAllText(path));
        return new SourceFile(
            Path.GetRelativePath(SrcRoot, path),
            relFolder,
            match.Success ? match.Groups[1].Value : null);
    }

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

    public sealed record SourceFile(string RelativePath, string RelativeFolder, string? DeclaredNamespace)
    {
        public string TopSegment => RelativeFolder.Split(Separators)[0];

        // The namespace the folder dictates: RemoteAgents + the src-relative folder path, dotted.
        public string ExpectedNamespace =>
            "RemoteAgents." + RelativeFolder.Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.');
    }
}
