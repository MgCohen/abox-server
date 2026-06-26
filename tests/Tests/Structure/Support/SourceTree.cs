namespace ABox.Tests.Structure.Support;

// The physical source layout on disk — the placement guards' input, independent of what compiled. ArchUnitNET
// only sees loaded assemblies; this sees every project folder under src/ the moment it exists, so placement
// can't be dodged by not building, and stray build output (gitignored, invisible to the reference graph) is
// still caught. The repo root is located once in Harness.RepoTree; this owns the src/ queries.
internal static class SourceTree
{
    private static readonly char[] Separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    public static readonly string Root = RepoTree.Root;
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

    public static readonly string FeaturesRoot = RequireDir(Path.Combine("src", "Features"));

    public static IReadOnlyList<string> FeatureFolders() =>
        Directory.EnumerateDirectories(FeaturesRoot)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    // The two published leaf-folder roles a feature may carry: the external Api leaf (client-facing) and the
    // internal Contract leaf (cross-feature). Every other project folder is implementation.
    private static readonly string[] LeafFolders = { "Api", "Contract" };

    // The csproj shape of one feature folder: every implementation project (anywhere but under an Api/ or
    // Contract/ folder), the Api leaf, and the Contract leaf. The canonical slice (ADR 0011 D2, as amended by
    // the contract-publishing split) is one impl + at most one of each leaf, at least one leaf overall.
    public static FeatureProjects ProjectsOf(string feature)
    {
        var root = Path.Combine(FeaturesRoot, feature);
        var projects = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(NotIgnored)
            .ToList();
        var api = projects.Count(p => UnderRoleFolder(p, "Api"));
        var contract = projects.Count(p => UnderRoleFolder(p, "Contract"));
        return new FeatureProjects(projects.Count - api - contract, api, contract);
    }

    private static bool UnderRoleFolder(string path, string role) =>
        Path.GetRelativePath(FeaturesRoot, path).Split(Separators)
            .Any(seg => string.Equals(seg, role, StringComparison.Ordinal));

    private static bool UnderAnyLeafFolder(string path) =>
        LeafFolders.Any(role => UnderRoleFolder(path, role));

    // The folders inside a feature that are NOT verbs: the published Api/Contract leaves and the folded-in Module.
    // Every other immediate folder is a verb and must carry its endpoint (the canonical slice has one per verb).
    private static readonly string[] NonVerbFolders = { "Api", "Contract", "Module" };

    // Verb folders of one feature that declare no endpoint: every immediate subfolder except Contracts/Module that
    // holds no `*Endpoint.cs`. A folder with no endpoint is either a stray (Flows' Shared) or a verb whose endpoint
    // is misnamed/misplaced — both break the "every verb folder looks the same" guarantee.
    public static IReadOnlyList<string> VerbFoldersWithoutEndpoint(string feature)
    {
        var root = Path.Combine(FeaturesRoot, feature);
        return Directory.EnumerateDirectories(root)
            .Where(dir => !NonVerbFolders.Contains(Path.GetFileName(dir), StringComparer.Ordinal))
            .Where(dir => !RepoTree.BuildOutputDirs.Contains(Path.GetFileName(dir)!, StringComparer.OrdinalIgnoreCase))
            .Where(dir => !Directory.EnumerateFiles(dir, "*Endpoint.cs", SearchOption.AllDirectories).Any())
            .Select(dir => Path.GetFileName(dir)!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    // A type whose name carries a wire role — request, response, DTO, or projection view — that the client or a peer
    // slice binds. The canonical slice keeps every one of these in an Api or Contract leaf; the suffixes match the
    // repo's own naming (CreateProjectRequest, ProjectDto, StartRunResponse, FlowView).
    private static readonly string[] ContractSuffixes = { "Request", "Response", "Dto", "View" };

    public static IReadOnlyList<string> ContractTypeFilesOutsideLeaves() => ContractTypeFiles(underLeaf: false);

    public static IReadOnlyList<string> ContractTypeFilesInLeaves() => ContractTypeFiles(underLeaf: true);

    private static IReadOnlyList<string> ContractTypeFiles(bool underLeaf) =>
        Directory.EnumerateFiles(FeaturesRoot, "*.cs", SearchOption.AllDirectories)
            .Where(NotIgnoredUnder(FeaturesRoot))
            .Where(IsContractTypeFile)
            .Where(f => UnderAnyLeafFolder(f) == underLeaf)
            .Select(f => Path.GetRelativePath(SrcRoot, f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    private static bool IsContractTypeFile(string csproj) =>
        ContractSuffixes.Any(s => Path.GetFileNameWithoutExtension(csproj).EndsWith(s, StringComparison.Ordinal));

    // The single packaging project that ships off-box: src/Api/ABox.Api.csproj, the rollup that bundles every
    // Api leaf into one .nupkg. The Api rollup is the ONE place IsPackable=true is allowed in the repo.
    public static readonly string ApiRollup = Path.Combine(SrcRoot, "Api", "ABox.Api.csproj");

    public static bool ApiRollupIsPackable() =>
        File.Exists(ApiRollup)
        && File.ReadAllText(ApiRollup) is var text
        && text.Contains("<IsPackable>true</IsPackable>", StringComparison.Ordinal)
        && text.Contains("<PackageId>ABox.Api</PackageId>", StringComparison.Ordinal);

    // Feature projects that opt INTO packing — none may, since the rollup is the sole package and every feature
    // project is held IsPackable=false by src/Features/Directory.Build.props. A `<IsPackable>true` here is a leak.
    public static IReadOnlyList<string> FeatureProjectsDeclaringPackable() =>
        FeatureCsprojs()
            .Where(p => File.ReadAllText(p).Contains("<IsPackable>true</IsPackable>", StringComparison.Ordinal))
            .Select(RelToSrc)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

    // Api leaf csprojs (Features/<F>/Api/*.Api.csproj). The published surface, discovered the same way the rollup's
    // wildcard discovers them — so the test's set and the rollup's set are computed from one shape.
    public static IReadOnlyList<string> ApiLeafCsprojs() =>
        FeatureCsprojs()
            .Where(p => UnderRoleFolder(p, "Api"))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

    // Api leaves the rollup's wildcard `Features/*/Api/*.Api.csproj` would MISS: a leaf whose path isn't
    // Features/<F>/Api/ or whose file isn't ABox.<F>.Api.csproj silently drops out of the package.
    public static IReadOnlyList<string> MisplacedApiLeaves() =>
        ApiLeafCsprojs()
            .Where(p => !IsCanonicalApiLeaf(p))
            .Select(RelToSrc)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

    private static bool IsCanonicalApiLeaf(string csproj)
    {
        var rel = Path.GetRelativePath(FeaturesRoot, csproj).Split(Separators);
        return rel.Length == 3
            && string.Equals(rel[1], "Api", StringComparison.Ordinal)
            && string.Equals(rel[2], $"ABox.{rel[0]}.Api.csproj", StringComparison.Ordinal);
    }

    // Api leaves that declare any dependency. An Api leaf must be a pure-DTO assembly with no ProjectReference or
    // PackageReference, so the rollup that bundles its DLL stays self-contained (no hidden transitive deps to ship).
    public static IReadOnlyList<string> ApiLeavesWithDependencies() =>
        ApiLeafCsprojs()
            .Where(p => File.ReadAllText(p) is var t
                && (t.Contains("<ProjectReference", StringComparison.Ordinal)
                    || t.Contains("<PackageReference", StringComparison.Ordinal)))
            .Select(RelToSrc)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

    private static IEnumerable<string> FeatureCsprojs() =>
        Directory.EnumerateFiles(FeaturesRoot, "*.csproj", SearchOption.AllDirectories).Where(NotIgnored);

    private static string RelToSrc(string path) => Path.GetRelativePath(SrcRoot, path);

    private static Func<string, bool> NotIgnoredUnder(string root) =>
        path => !Path.GetRelativePath(root, path).Split(Separators)
            .Any(seg => RepoTree.BuildOutputDirs.Contains(seg, StringComparer.OrdinalIgnoreCase));

    // The top-most bin/obj/artifacts folders found under src/ or tests/. The only legal artifacts home is the
    // repo-root /artifacts; any here means a project escaped the root props.
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
        return RepoTree.BuildOutputDirs.Contains(segments[^1], StringComparer.OrdinalIgnoreCase)
            && !segments[..^1].Any(s => RepoTree.BuildOutputDirs.Contains(s, StringComparer.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> Projects() =>
        Directory.EnumerateFiles(SrcRoot, "*.csproj", SearchOption.AllDirectories).Where(NotIgnored);

    private static bool NotIgnored(string path) =>
        !Path.GetRelativePath(SrcRoot, path).Split(Separators)
            .Any(seg => RepoTree.BuildOutputDirs.Contains(seg, StringComparer.OrdinalIgnoreCase));

    private static string TopSegment(string path) =>
        Path.GetRelativePath(SrcRoot, path).Split(Separators)[0];

    private static string RequireDir(string name) =>
        Directory.Exists(Path.Combine(Root, name))
            ? Path.Combine(Root, name)
            : throw new DirectoryNotFoundException(
                $"Found the repo root at '{Root}' but no '{name}/' beside it — the structure guard can't run.");
}
