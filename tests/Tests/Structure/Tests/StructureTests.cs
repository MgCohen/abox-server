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

    [Rule("Each verb folder declares its endpoint")]
    [Fact]
    public void EachVerbFolderDeclaresItsEndpoint()
    {
        var canonical = SourceTree.FeatureFolders().Where(f => !IsPendingConsolidation(f)).ToList();
        Assert.Contains("Projects", canonical);

        var strays = canonical
            .Select(f => (Feature: f, Folders: SourceTree.VerbFoldersWithoutEndpoint(f)))
            .Where(x => x.Folders.Count > 0)
            .Select(x => $"{x.Feature}: {string.Join(", ", x.Folders)}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(strays.Count == 0,
            $"""
            These verb folders carry no '*Endpoint.cs' — the canonical slice is one endpoint per verb folder, so a
            folder with none is a stray (e.g. a 'Shared' helper bucket) or a verb whose endpoint is misplaced/misnamed:
            {Bullets(strays)}
            Give the verb folder its '<Verb>Endpoint', or move the helper to the Module/Domain/Infrastructure.
            """);
    }

    [Rule("Requests, responses, and DTOs live in the Contracts leaf")]
    [Fact]
    public void ContractTypesLiveInContracts()
    {
        // Non-vacuity: the naming convention is live — the Contracts leaves do hold these types, so the rule below
        // is policing a real population, not passing because no contract types exist anywhere.
        Assert.NotEmpty(SourceTree.ContractTypeFilesInContracts());

        var strays = SourceTree.ContractTypeFilesOutsideContracts();
        Assert.True(strays.Count == 0,
            $"""
            These request/response/DTO/view types live outside a Contracts/ folder — the client and peer slices bind
            the Contracts leaf, so a wire type stranded in a verb folder is unbindable:
            {Bullets(strays)}
            Move each into the feature's Contracts/ leaf.
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
