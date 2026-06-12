using System.Reflection;

namespace RemoteAgents.Tests.Harness;

// The parity engine shared by every test type. A Rulebook (Rulebook/rules.md) lists a type's guarantees as
// '### ' headers — the DECLARED rules; the [Rule("<header>")] tests that enforce them are the ENFORCED
// rules. For(anchor) scopes discovery to the anchor type's namespace, so multiple Rulebooks can live in one
// assembly without bleeding into each other's parity. Assert fails the build on any mismatch.
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
    // be realized by several case tests.
    public static ParityGuard For(Type anchor, bool strict = false) => new(anchor, strict);

    public void Assert(string rulebookPath)
    {
        var declared = DeclaredRules(rulebookPath);
        var enforced = EnforcedRules();

        var unenforced = declared.Except(enforced).ToList();
        var undocumented = enforced.Except(declared).ToList();
        var duplicated = strict
            ? enforced.GroupBy(name => name).Where(g => g.Count() > 1).Select(g => g.Key).ToList()
            : [];

        Xunit.Assert.True(
            unenforced.Count == 0 && undocumented.Count == 0 && duplicated.Count == 0,
            $"""
            Rulebook ({rulebookPath}) and its [Rule] tests are out of sync:
              Rules with no test:          {Fmt(unenforced)}
              tests citing a missing Rule: {Fmt(undocumented)}
              Rules tested more than once: {Fmt(duplicated)}
            Fix: align each '### <name>' header with a [Rule("<name>")] test so the names match exactly.
            """);
    }

    private IReadOnlyList<string> EnforcedRules()
    {
        var scope = anchor.Namespace ?? "";
        return anchor.Assembly.GetTypes()
            .Where(t => InScope(t.Namespace, scope))
            .SelectMany(t => t.GetMethods())
            .Select(m => m.GetCustomAttribute<Rule>())
            .Where(a => a is not null)
            .Select(a => a!.Name)
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
            .Where(l => l.StartsWith(Heading, StringComparison.Ordinal))
            .Select(l => l[Heading.Length..].Trim())
            .ToList();
    }

    private static string Fmt(IReadOnlyList<string> names) =>
        names.Count == 0 ? "(none)" : string.Join(", ", names.Select(n => $"\"{n}\""));
}
