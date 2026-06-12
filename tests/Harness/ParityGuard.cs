using System.Reflection;

namespace RemoteAgents.Tests.Harness;

// The parity engine shared by every test type. A Rulebook (Rulebook/rules.md) lists a type's guarantees as
// '### ' headers — the DECLARED rules; the [Rule("<header>")] citations sitting on its tests are the ENFORCED
// rules. For(anchor) scopes discovery to the anchor type's namespace, so multiple Rulebooks can live in one
// assembly without bleeding into each other's parity. Assert fails the build on any mismatch — including a
// test with no [Rule] (requireAllCited) and a [Rule] that sits on no runnable test.
public sealed class ParityGuard
{
    private const string Heading = "### ";

    private readonly Type anchor;
    private readonly bool strict;

    private ParityGuard(Type anchor, bool strict)
    {
        this.anchor = anchor;
        this.strict = strict;
    }

    // strict = 1:1 (every Rule has exactly one test) for invariant types (Arch, Structure). The default 1:N
    // (every Rule has >=1 test, every test cites a real Rule) fits behavioral types, where one guarantee may
    // be realized by several case tests. requireAllCited turns on the completeness guard: no bare test.
    public static ParityGuard For(Type anchor, bool strict = false) => new(anchor, strict);

    public void Assert(string rulebookPath, bool requireAllCited = false)
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
        var duplicated = strict
            ? enforced.GroupBy(name => name).Where(g => g.Count() > 1).Select(g => g.Key).ToList()
            : [];
        var orphaned = methods
            .Where(m => m.GetCustomAttribute<Rule>() is not null && !TestMarkers.Marks(m))
            .Select(m => m.Name)
            .ToList();
        var uncited = requireAllCited
            ? methods.Where(TestMarkers.Marks).Where(m => m.GetCustomAttribute<Rule>() is null).Select(m => m.Name).ToList()
            : [];

        Xunit.Assert.True(
            unenforced.Count == 0 && undocumented.Count == 0 && duplicated.Count == 0
                && orphaned.Count == 0 && uncited.Count == 0,
            $"""
            Rulebook ({rulebookPath}) and its [Rule] tests are out of sync — fix each non-empty list:
              Add a [Rule]-cited test for the header:          {Fmt(unenforced)}
              Add a '### ' header (or correct the citation) for: {Fmt(undocumented)}
              Remove the duplicate citation of the Rule:       {Fmt(duplicated)}
              Add a test attribute ([Fact]/[Theory]) to the [Rule] method: {Fmt(orphaned)}
              Add a [Rule("<header>")] to the bare test:        {Fmt(uncited)}
            Each '### <name>' header pairs 1:{(strict ? "1" : "N")} with a [Rule("<name>")] on a runnable test.
            """);
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

        // Skip fenced ``` blocks so a Rulebook's self-teaching template can show a real '### ' example
        // without it being counted as a declared Rule.
        var rules = new List<string>();
        var inFence = false;
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
                inFence = !inFence;
            else if (!inFence && line.StartsWith(Heading, StringComparison.Ordinal))
                rules.Add(line[Heading.Length..].Trim());
        }

        return rules;
    }

    private static string Fmt(IReadOnlyList<string> names) =>
        names.Count == 0 ? "(none)" : string.Join(", ", names.Select(n => $"\"{n}\""));
}
