using System.Reflection;

namespace ABox.Tests.Harness;

// The parity engine: it keeps one test type's Rulebook (Rulebook/rules.md) and the [Rule]-cited tests that
// enforce it in lockstep, scoped to a single namespace so types in the shared assembly don't bleed into each
// other's parity. The Meta type drives this once over every registered type.
public sealed class ParityGuard
{
    private const string Heading = "### ";

    private readonly Assembly assembly;
    private readonly string scope;
    private readonly string rulebookPath;

    private ParityGuard(Assembly assembly, string scope, string rulebookPath)
    {
        this.assembly = assembly;
        this.scope = scope;
        this.rulebookPath = rulebookPath;
    }

    public static ParityGuard For(Assembly assembly, string type) =>
        new(assembly, TestTypes.Namespace(type), TestTypes.RulebookPath(type));

    public void Assert(bool requireAllCited = false)
    {
        var declared = DeclaredRules(rulebookPath);
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
        var uncited = requireAllCited
            ? methods.Where(TestMarkers.Marks).Where(m => m.GetCustomAttribute<Rule>() is null).Select(m => m.Name).ToList()
            : [];

        Xunit.Assert.True(
            unenforced.Count == 0 && undocumented.Count == 0 && orphaned.Count == 0 && uncited.Count == 0,
            $"""
            Rulebook ({rulebookPath}) and its [Rule] tests are out of sync — fix each non-empty list:
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

    private static IReadOnlyList<string> DeclaredRules(string rulebookPath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, rulebookPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Rulebook not found at '{path}'. The csproj must copy it to the output directory " +
                "(a None item with CopyToOutputDirectory).");

        return File.ReadAllLines(path)
            .Where(line => line.StartsWith(Heading, StringComparison.Ordinal))
            .Select(line => line[Heading.Length..].Trim())
            .ToList();
    }

    private static string Fmt(IReadOnlyList<string> names) =>
        names.Count == 0 ? "(none)" : string.Join(", ", names.Select(n => $"\"{n}\""));
}
