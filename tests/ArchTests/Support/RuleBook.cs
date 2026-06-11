using System.Reflection;

namespace RemoteAgents.Tests.ArchTests;

// The rule book: the '### ' headers in Fixtures/rules.md are the declared architecture rules. The file
// is copied next to the assembly at build time (see csproj). Each header is enforced by exactly one
// [Rule]-tagged test, linked by name — RuleBookTests guards that correspondence.
internal static class RuleBook
{
    private const string Heading = "### ";

    public static IReadOnlyList<string> DeclaredRules()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "rules.md");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Rule book not found at '{path}'. The csproj must copy Fixtures/*.md to the output directory.");

        return File.ReadAllLines(path)
            .Where(l => l.StartsWith(Heading, StringComparison.Ordinal))
            .Select(l => l[Heading.Length..].Trim())
            .ToList();
    }

    public static IReadOnlyList<string> EnforcedRules() =>
        Assembly.GetExecutingAssembly()
            .GetTypes()
            .SelectMany(t => t.GetMethods())
            .Select(m => m.GetCustomAttribute<RuleAttribute>())
            .Where(a => a is not null)
            .Select(a => a!.Name)
            .ToList();
}
