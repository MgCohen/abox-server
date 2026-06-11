using static RemoteAgents.Tests.ArchTests.ArchitectureModel;

namespace RemoteAgents.Tests.ArchTests;

// Physical-placement guards: they read folders/files on disk (SourceTree), not the loaded assembly
// graph, so they catch drift the moment it lands — even uncompiled or arch-excluded code (Web, Morph).
public class StructureTests
{
    [Rule("Every project lives under an agreed home folder")]
    public void EveryProjectUnderAHomeFolder()
    {
        var strays = SourceTree.ProjectTopSegments()
            .Where(seg => !IsHomeFolder(seg) && !IsPendingEviction(seg))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(strays.Count == 0,
            $"""
            Projects live under folders that are not an agreed home:
            {Bullets(strays)}
            Agreed home folders:
            {Bullets(AgreedHomeFolders)}
            """);

        // The eviction list must not outlive its folders — a stale entry is a silent allow-hole.
        var stale = PendingEvictionFolders.Where(f => !SourceTree.HasTopSegment(f)).ToList();
        Assert.True(stale.Count == 0,
            $"""
            These folders are gone but still listed in PendingEvictionFolders:
            {Bullets(stale)}
            Drop them from ArchitectureModel.PendingEvictionFolders.
            """);
    }

    [Rule("A type's namespace mirrors its folder")]
    public void NamespaceMirrorsFolder()
    {
        var offenders = SourceTree.SourceFiles()
            .Where(f => f.DeclaredNamespace is not null && !IsPendingEviction(f.TopSegment))
            .Where(f => f.DeclaredNamespace != f.ExpectedNamespace)
            .Select(f => $"{f.RelativePath}: declares '{f.DeclaredNamespace}', folder expects '{f.ExpectedNamespace}'")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"""
            Files whose namespace does not mirror their folder:
            {Bullets(offenders)}
            Fix: set the namespace to RemoteAgents + the src-relative folder path (or move the file).
            """);
    }

    private static string Bullets(IEnumerable<string> items) =>
        string.Join(Environment.NewLine, items.Select(i => $"  * {i}"));
}
