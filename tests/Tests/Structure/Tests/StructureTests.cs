using ABox.Tests.Structure.Support;
using static ABox.Tests.Harness.Report;
using static ABox.Tests.Structure.Support.HomeFolders;
using static ABox.Tests.Structure.Support.FeatureShape;

namespace ABox.Tests.Structure.Tests;

// Source-placement guard: reads the project folders on disk (SourceTree), not the loaded assembly graph, so it
// catches a stray project the moment it lands — even uncompiled or arch-excluded code. The test-system's own
// layout (taxonomy, Rulebook format) is the Meta type's job, not this one.
public class StructureTests
{
    [Rule("Every project lives under an agreed home folder")]
    [Fact]
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

    [Rule("Each feature is one implementation project plus one Contracts leaf")]
    [Fact]
    public void EachFeatureIsOneImplPlusOneContracts()
    {
        var features = SourceTree.FeatureFolders();
        Assert.Contains("Projects", features);

        var strays = features
            .Where(f => !IsPendingConsolidation(f))
            .Where(f => !SourceTree.ProjectsOf(f).IsCanonical)
            .Select(f => $"{f} ({SourceTree.ProjectsOf(f)})")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(strays.Count == 0,
            $"""
            Features do not match the canonical shape — exactly one implementation project (verbs as
            folders, Module folded in) + one Contracts leaf (ADR 0011 D2):
            {Bullets(strays)}
            Consolidate the feature to two projects, or list it in FeatureShape.PendingConsolidation.
            """);

        // The consolidation list must not outlive the split — a feature that became canonical but is still
        // listed is a silent allow-hole.
        var stale = PendingConsolidation
            .Where(features.Contains)
            .Where(f => SourceTree.ProjectsOf(f).IsCanonical)
            .ToList();
        Assert.True(stale.Count == 0,
            $"""
            These features are now one impl + one Contracts but still listed pending consolidation:
            {Bullets(stale)}
            Drop them from FeatureShape.PendingConsolidation.
            """);
    }

    [Rule("No build output lives under src or tests")]
    [Fact]
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
}
