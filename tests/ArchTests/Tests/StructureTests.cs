using static RemoteAgents.Tests.ArchTests.ArchitectureModel;

namespace RemoteAgents.Tests.ArchTests;

// Physical-placement guard: reads the project folders on disk (SourceTree), not the loaded assembly
// graph, so it catches a stray project the moment it lands — even uncompiled or arch-excluded code
// (Web, Morph). Namespace-matches-folder is enforced separately by IDE0130 at compile time.
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

    private static string Bullets(IEnumerable<string> items) =>
        string.Join(Environment.NewLine, items.Select(i => $"  * {i}"));
}
