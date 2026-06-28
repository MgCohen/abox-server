using System.Reflection;

namespace ABox.Tests.Harness;

// The parity engine: it keeps one test type's Rulebook (Rulebook.md) and the [Rule]-cited tests that
// enforce it in lockstep, scoped to a single namespace so types sharing an assembly don't bleed into each
// other's parity. The harness's own tests drive this over every central type (For) and every
// co-located type (ForColocated).
public sealed class ParityGuard
{
    private const string Heading = "### ";

    private readonly Assembly assembly;
    private readonly string scope;
    private readonly string rulesPath;

    private ParityGuard(Assembly assembly, string scope, string rulesPath)
    {
        this.assembly = assembly;
        this.scope = scope;
        this.rulesPath = rulesPath;
    }

    public static ParityGuard For(Assembly assembly, string type) =>
        new(assembly, TestTypes.Namespace(type), ProductRulebook(type));

    // A co-located feature assembly (ABox.<Owner>.Tests) keeps each type's tests in the <Owner>.Tests.<Type>
    // namespace and its Rulebook beside them under Tests/<Type>/Rulebook.md. The scope is the assembly
    // name + the type; the Rulebook is found from the source tree via the TestsSourceDir the build stamps, so
    // parity reads the same on-disk Rulebook.md the doc-engine validates — no copy-to-output step.
    public static ParityGuard ForColocated(Assembly assembly, string type) =>
        new(assembly, TestTypes.ColocatedNamespace(assembly.GetName().Name!, type), ColocatedRulebook(assembly, type));

    private const string TestsSourceDirKey = "TestsSourceDir";

    private static string ColocatedRulebook(Assembly assembly, string type)
    {
        var sourceDir = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == TestsSourceDirKey)?.Value
            ?? throw new InvalidOperationException(
                $"Assembly '{assembly.GetName().Name}' carries no [{TestsSourceDirKey}] metadata. A co-located " +
                "feature test project must stamp <AssemblyMetadata Include=\"TestsSourceDir\" " +
                "Value=\"$(MSBuildProjectDirectory)\" /> so parity can find its Rulebook in the source tree.");
        return Path.Combine(sourceDir, type, "Rulebook.md");
    }

    // Product Rulebooks are read from the source tree, not the output dir — the same surface the harness's own
    // tests already read, so they need no copy of the product's Rulebooks to validate them.
    private static string ProductRulebook(string type) =>
        Path.Combine(RepoTree.TestsRoot, TestTypes.RulebookPath(type).Replace('/', Path.DirectorySeparatorChar));

    public void Assert()
    {
        var declared = DeclaredRules(rulesPath);
        var methods = ScopedMethods();

        var enforced = methods
            .Select(m => m.GetCustomAttribute<Rule>())
            .Where(a => a is not null)
            .Select(a => a!.Name)
            .ToList();

        var unenforced = declared.Except(enforced).ToList();
        var undocumented = enforced.Except(declared).ToList();
        var orphaned = methods
            .Where(m => m.GetCustomAttribute<Rule>() is not null && !TestMarkers.Marks(m))
            .Select(m => m.Name)
            .ToList();
        var uncited = methods.Where(TestMarkers.Marks)
            .Where(m => m.GetCustomAttribute<Rule>() is null).Select(m => m.Name).ToList();

        Xunit.Assert.True(
            unenforced.Count == 0 && undocumented.Count == 0 && orphaned.Count == 0 && uncited.Count == 0,
            $"""
            Rulebook ({Path.GetRelativePath(RepoTree.Root, rulesPath)}) and its [Rule] tests are out of sync — fix each non-empty list:
              Add a [Rule]-cited test for the header:          {Fmt(unenforced)}
              Add a '### ' header (or correct the citation) for: {Fmt(undocumented)}
              Add a test attribute ([Fact]/[Theory]) to the [Rule] method: {Fmt(orphaned)}
              Add a [Rule("<header>")] to the bare test:        {Fmt(uncited)}
            Each '### <name>' header pairs 1:N with a [Rule("<name>")] on a runnable test.
            """);
    }

    private IReadOnlyList<MethodInfo> ScopedMethods() =>
        assembly.GetTypes()
            .Where(t => InScope(t.Namespace))
            .SelectMany(t => t.GetMethods())
            .ToList();

    private bool InScope(string? ns) =>
        ns is not null && (ns == scope || ns.StartsWith(scope + ".", StringComparison.Ordinal));

    private static IReadOnlyList<string> DeclaredRules(string rulesPath)
    {
        if (!File.Exists(rulesPath))
            throw new FileNotFoundException(
                $"Rulebook not found at '{rulesPath}'. Parity reads Rulebook.md from the source tree (RepoTree); " +
                "check the type's folder layout or the repo-root locator.");

        return File.ReadAllLines(rulesPath)
            .Where(line => line.StartsWith(Heading, StringComparison.Ordinal))
            .Select(line => line[Heading.Length..].Trim())
            .ToList();
    }

    private static string Fmt(IReadOnlyList<string> names) =>
        names.Count == 0 ? "(none)" : string.Join(", ", names.Select(n => $"\"{n}\""));
}
