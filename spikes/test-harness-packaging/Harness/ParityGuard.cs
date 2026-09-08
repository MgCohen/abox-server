using System.Reflection;

namespace Kit.Tests.Harness;

// The parity ENGINE, lifted from tests/Harness/Tests/ParityGuard.cs. Its core — declared '### ' headers vs the
// [Rule]-cited tests, and the four out-of-sync lists (unenforced / undocumented / orphaned / uncited) — is
// verbatim. Two seams change for a standalone consumer:
//   * scope is resolved against the ONE consumer assembly passed in, not a multi-assembly Suites discovery;
//   * the Rulebook is found via a `RulebookDir` AssemblyMetadata the consumer stamps (the same metadata-seam the
//     Phase 1/3 spikes use), instead of the repo's TestsSourceDir + RepoTree pairing.
public sealed class ParityGuard
{
    private const string Heading = "### ";
    private const string RulebookDirKey = "RulebookDir";

    private readonly Assembly assembly;
    private readonly string scope;
    private readonly string rulesPath;

    private ParityGuard(Assembly assembly, string scope, string rulesPath)
    {
        this.assembly = assembly;
        this.scope = scope;
        this.rulesPath = rulesPath;
    }

    // scope is <AssemblyName>.<Type>, the same convention as the in-repo harness; the Rulebook sits at
    // <RulebookDir>/<Type>/Rulebook.md in the source tree the consumer stamps.
    public static ParityGuard For(Assembly assembly, string type) =>
        new(assembly, $"{assembly.GetName().Name}.{type}", Rulebook(assembly, type));

    private static string Rulebook(Assembly assembly, string type)
    {
        var sourceDir = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == RulebookDirKey)?.Value
            ?? throw new InvalidOperationException(
                $"Assembly '{assembly.GetName().Name}' carries no [{RulebookDirKey}] metadata. A consumer suite must " +
                "stamp <AssemblyMetadata Include=\"RulebookDir\" Value=\"$(MSBuildProjectDirectory)\" /> so parity can " +
                "find its Rulebook in the source tree.");
        return Path.Combine(sourceDir, type, "Rulebook.md");
    }

    public void Assert()
    {
        var declared = DeclaredRules(rulesPath);
        var methods = ScopedMethods();

        var enforced = methods
            .Select(m => m.GetCustomAttribute<RuleAttribute>())
            .Where(a => a is not null)
            .Select(a => a!.Name)
            .ToList();

        var unenforced = declared.Except(enforced).ToList();
        var undocumented = enforced.Except(declared).ToList();
        var orphaned = methods
            .Where(m => m.GetCustomAttribute<RuleAttribute>() is not null && !TestMarkers.Marks(m))
            .Select(m => m.Name)
            .ToList();
        var uncited = methods.Where(TestMarkers.Marks)
            .Where(m => m.GetCustomAttribute<RuleAttribute>() is null).Select(m => m.Name).ToList();

        Xunit.Assert.True(
            unenforced.Count == 0 && undocumented.Count == 0 && orphaned.Count == 0 && uncited.Count == 0,
            $"""
            Rulebook ({rulesPath}) and its [Rule] tests are out of sync — fix each non-empty list:
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
                $"Rulebook not found at '{rulesPath}'. Parity reads Rulebook.md from the source tree; check the " +
                "type's folder layout or the RulebookDir metadata.");

        return File.ReadAllLines(rulesPath)
            .Where(line => line.StartsWith(Heading, StringComparison.Ordinal))
            .Select(line => line[Heading.Length..].Trim())
            .ToList();
    }

    private static string Fmt(IReadOnlyList<string> names) =>
        names.Count == 0 ? "(none)" : string.Join(", ", names.Select(n => $"\"{n}\""));
}
