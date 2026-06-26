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

    [Rule("Each feature is one implementation project plus its Api/Contract leaves")]
    [Fact]
    public void EachFeatureIsOneImplPlusItsLeaves()
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
            Features do not match the canonical shape — exactly one implementation project (verbs as folders,
            Module folded in) + at most one Api leaf and at most one Contract leaf, at least one leaf
            (ADR 0011 D2, amended by the contract-publishing split):
            {Bullets(strays)}
            Consolidate the feature's implementation to one project, or list it in FeatureShape.PendingConsolidation.
            """);

        // The consolidation list must not outlive the split — a feature that became canonical but is still
        // listed is a silent allow-hole.
        var stale = PendingConsolidation
            .Where(features.Contains)
            .Where(f => SourceTree.ProjectsOf(f).IsCanonical)
            .ToList();
        Assert.True(stale.Count == 0,
            $"""
            These features are now one impl + their leaves but still listed pending consolidation:
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

    [Rule("Requests, responses, and DTOs live in an Api or Contract leaf")]
    [Fact]
    public void ContractTypesLiveInLeaves()
    {
        // Non-vacuity: the naming convention is live — the leaves do hold these types, so the rule below is
        // policing a real population, not passing because no contract types exist anywhere.
        Assert.NotEmpty(SourceTree.ContractTypeFilesInLeaves());

        var strays = SourceTree.ContractTypeFilesOutsideLeaves();
        Assert.True(strays.Count == 0,
            $"""
            These request/response/DTO/view types live outside an Api/ or Contract/ leaf — the client binds the Api
            leaf and a peer slice binds the Contract leaf, so a wire type stranded in a verb folder is unbindable:
            {Bullets(strays)}
            Move each into the feature's Api/ leaf (client-facing) or Contract/ leaf (cross-feature).
            """);
    }

    [Rule("Only the Api rollup is packable")]
    [Fact]
    public void OnlyTheApiRollupIsPackable()
    {
        Assert.True(SourceTree.ApiRollupIsPackable(),
            $"""
            The publishing rollup '{Path.GetRelativePath(SourceTree.Root, SourceTree.ApiRollup)}' must exist and
            declare <IsPackable>true</IsPackable> + <PackageId>ABox.Api</PackageId> — it is the single package that
            ships off-box, bundling every Api leaf's DLL.
            """);

        var leaks = SourceTree.FeatureProjectsDeclaringPackable();
        Assert.True(leaks.Count == 0,
            $"""
            These feature projects opt into packing — only the ABox.Api rollup may. A feature project that packs
            individually would publish a second, unmanaged package to the feed:
            {Bullets(leaks)}
            Remove the <IsPackable>true</IsPackable>; src/Features/Directory.Build.props already holds them false.
            """);
    }

    [Rule("Every Api leaf is a self-contained bundle input")]
    [Fact]
    public void EveryApiLeafIsASelfContainedBundleInput()
    {
        // Non-vacuity: there is at least one Api leaf, so the placement/dependency checks below police a real set.
        Assert.NotEmpty(SourceTree.ApiLeafCsprojs());

        var misplaced = SourceTree.MisplacedApiLeaves();
        Assert.True(misplaced.Count == 0,
            $"""
            These Api leaves sit where the rollup's wildcard (Features/*/Api/*.Api.csproj) would MISS them, so they
            would silently drop out of the ABox.Api package:
            {Bullets(misplaced)}
            Place each at Features/<F>/Api/ABox.<F>.Api.csproj.
            """);

        var withDeps = SourceTree.ApiLeavesWithDependencies();
        Assert.True(withDeps.Count == 0,
            $"""
            These Api leaves declare a Project/PackageReference — an Api leaf must be a pure-DTO assembly so the
            rollup that bundles its DLL stays self-contained (a hidden dep would not ship and would break the client):
            {Bullets(withDeps)}
            Remove the reference, or move the shared type into the leaf.
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
