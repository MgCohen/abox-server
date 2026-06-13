using System.Reflection;

namespace ABox.Tests.Harness;

// The parity engine shared by every test type: it keeps a type's Rulebook (Rulebook/rules.md) and the
// [Rule]-cited tests enforcing it in lockstep, scoped to the anchor type's namespace so multiple Rulebooks
// coexist in one assembly without bleeding into each other's parity.
public sealed class ParityGuard
{
    private const string Heading = "### ";

    private readonly Type anchor;

    private ParityGuard(Type anchor) => this.anchor = anchor;

    public static ParityGuard For(Type anchor) => new(anchor);

    public void Assert(bool requireAllCited = false)
    {
        var rulebookPath = DeriveRulebookPath(anchor);
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

    // ABox.Tests.<Type>.Tests -> <Type>/Rulebook/rules.md. The path drifts with the folder; IDE0130 keeps
    // the namespace mirroring the folder, so deriving it here means no literal to hand-sync at a call site.
    private static string DeriveRulebookPath(Type anchor)
    {
        var parts = (anchor.Namespace ?? "").Split('.');
        if (parts.Length < 4 || parts[0] != "ABox" || parts[1] != "Tests" || parts[^1] != "Tests")
            throw new ArgumentException(
                $"Cannot derive a Rulebook path from namespace '{anchor.Namespace}'. A Parity anchor must live " +
                "in 'ABox.Tests.<Type>.Tests' so the path resolves to '<Type>/Rulebook/rules.md'.", nameof(anchor));
        return $"{parts[2]}/Rulebook/rules.md";
    }

    private IReadOnlyList<MethodInfo> ScopedMethods()
    {
        var scope = anchor.Namespace ?? "";
        return anchor.Assembly.GetTypes()
            .Where(t => InScope(t.Namespace, scope))
            .SelectMany(t => t.GetMethods())
            .ToList();
    }

    private static bool InScope(string? ns, string scope) =>
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
