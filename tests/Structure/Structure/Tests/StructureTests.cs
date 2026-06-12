using RemoteAgents.Tests.Structure.Support;
using static RemoteAgents.Tests.Structure.Support.HomeFolders;

namespace RemoteAgents.Tests.Structure.Tests;

// Physical-placement guard: reads the project folders on disk (SourceTree), not the loaded assembly
// graph, so it catches a stray project the moment it lands — even uncompiled or arch-excluded code.
// Namespace-matches-folder is enforced separately by IDE0130 at compile time.
public class StructureTests
{
    [Rule("Every project lives under an agreed home folder")]
    public void EveryProjectUnderAHomeFolder()
    {
        var strays = SourceTree.ProjectTopSegments()
            .Where(seg => !IsHome(seg) && !IsPendingEviction(seg))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(strays.Count == 0,
            $"""
            Projects live under folders that are not an agreed home:
            {Bullets(strays)}
            Agreed home folders:
            {Bullets(Agreed)}
            """);

        // The eviction list must not outlive its folders — a stale entry is a silent allow-hole.
        var stale = PendingEviction.Where(f => !SourceTree.HasTopSegment(f)).ToList();
        Assert.True(stale.Count == 0,
            $"""
            These folders are gone but still listed in PendingEviction:
            {Bullets(stale)}
            Drop them from HomeFolders.PendingEviction.
            """);
    }

    [Rule("No build output lives under src or tests")]
    public void NoBuildOutputUnderSource()
    {
        var strays = SourceTree.StrayBuildOutput();
        Assert.True(strays.Count == 0,
            $"""
            Build output (bin/obj/artifacts) escaped into the source tree:
            {Bullets(strays)}
            The only legal artifacts home is the repo-root /artifacts (UseArtifactsOutput + a pinned
            ArtifactsPath). A stray here means a project escaped the root Directory.Build.props.
            """);
    }

    private static string Bullets(IEnumerable<string> items) =>
        string.Join(Environment.NewLine, items.Select(i => $"  * {i}"));
}
