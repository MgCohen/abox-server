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

    // The csproj shape of one feature folder: every implementation project (anywhere but under a Contracts/
    // folder) and every Contracts-leaf project. The canonical slice (ADR 0011 D2) is exactly one of each.
    public static FeatureProjects ProjectsOf(string feature)
    {
        var root = Path.Combine(FeaturesRoot, feature);
        var projects = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(NotIgnored)
            .ToList();
        var contracts = projects.Where(UnderContractsFolder).ToList();
        var impl = projects.Except(contracts).ToList();
        return new FeatureProjects(impl.Count, contracts.Count);
    }

    private static bool UnderContractsFolder(string csproj) =>
        Path.GetRelativePath(FeaturesRoot, csproj).Split(Separators)
            .Any(seg => string.Equals(seg, "Contracts", StringComparison.Ordinal));

    // The folders inside a feature that are NOT verbs: the published Contracts leaf and the folded-in Module. Every
    // other immediate folder is a verb and must carry its endpoint (the canonical slice has one endpoint per verb).
    private static readonly string[] NonVerbFolders = { "Contracts", "Module" };

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
    // slice binds. The canonical slice keeps every one of these in the Contracts leaf; the suffixes match the repo's
    // own naming (CreateProjectRequest, ProjectDto, StartRunResponse, FlowView).
    private static readonly string[] ContractSuffixes = { "Request", "Response", "Dto", "View" };

    public static IReadOnlyList<string> ContractTypeFilesOutsideContracts() => ContractTypeFiles(underContracts: false);

    public static IReadOnlyList<string> ContractTypeFilesInContracts() => ContractTypeFiles(underContracts: true);

    private static IReadOnlyList<string> ContractTypeFiles(bool underContracts) =>
        Directory.EnumerateFiles(FeaturesRoot, "*.cs", SearchOption.AllDirectories)
            .Where(NotIgnoredUnder(FeaturesRoot))
            .Where(IsContractTypeFile)
            .Where(f => UnderContractsFolderRel(f) == underContracts)
            .Select(f => Path.GetRelativePath(SrcRoot, f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    private static bool IsContractTypeFile(string csproj) =>
        ContractSuffixes.Any(s => Path.GetFileNameWithoutExtension(csproj).EndsWith(s, StringComparison.Ordinal));

    private static bool UnderContractsFolderRel(string file) =>
        Path.GetRelativePath(FeaturesRoot, file).Split(Separators)
            .Any(seg => string.Equals(seg, "Contracts", StringComparison.Ordinal));

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
