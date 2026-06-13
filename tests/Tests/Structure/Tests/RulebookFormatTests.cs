using ABox.Tests.Structure.Support;

namespace ABox.Tests.Structure.Tests;

// Self-consistency of the Rulebooks themselves: every Rule matches its type's template.md schema, and each
// rules.md holds nothing but its title and Rules. Read from disk like every other placement guard, so a new
// test type is covered the moment its Rulebook folder lands — no per-type wiring.
public class RulebookFormatTests
{
    [Rule("Every Rule matches its type's template")]
    [Fact]
    public void EveryRuleMatchesItsTemplate()
    {
        var violations = new List<string>();
        foreach (var folder in SourceTree.RulebookFolders())
        {
            var schema = RulebookFormat.ReadSchema(Path.Combine(folder, "template.md"));
            var rulesPath = Path.Combine(folder, "rules.md");
            foreach (var rule in RulebookFormat.Rules(File.ReadAllLines(rulesPath)))
            {
                if (!rule.Labels.SetEquals(schema.Labels))
                    violations.Add($"{Rel(rulesPath)} :: \"{rule.Header}\" has bullets [{Join(rule.Labels)}], " +
                        $"template requires exactly [{Join(schema.Labels)}]");
                if (rule.HeaderHasArrow != schema.HeaderHasArrow)
                    violations.Add($"{Rel(rulesPath)} :: \"{rule.Header}\" " +
                        (schema.HeaderHasArrow ? "must end in a → result" : "must not contain a → (invariant shape)"));
            }
        }

        Assert.True(violations.Count == 0,
            $"""
            Rules drift from their type's template.md schema (format, not content):
            {Bullets(violations)}
            Match each Rule to its type's template: exactly the template's **bold-label** bullets, and the
            header's → arrow iff the template header carries one. Extra notes go in plain prose under the Rule.
            """);
    }

    [Rule("Every Rulebook holds only rules")]
    [Fact]
    public void EveryRulebookHoldsOnlyRules()
    {
        var violations = new List<string>();
        foreach (var folder in SourceTree.RulebookFolders())
        {
            var rulesPath = Path.Combine(folder, "rules.md");
            var headings = RulebookFormat.Headings(File.ReadAllLines(rulesPath));

            var titles = headings.Count(h => h.Level == 1);
            if (titles != 1)
                violations.Add($"{Rel(rulesPath)} :: must have exactly one '# ' title (found {titles})");

            foreach (var stray in headings.Where(h => h.Level is not 1 and not 3))
                violations.Add($"{Rel(rulesPath)} :: stray '{new string('#', stray.Level)} {stray.Text}' — " +
                    "only the '# ' title and '### ' Rules are allowed");
        }

        Assert.True(violations.Count == 0,
            $"""
            rules.md files contain headings other than the title and Rules:
            {Bullets(violations)}
            Keep rules.md to a '# ' title, a short preamble, and '### ' Rules. Move sections elsewhere.
            """);
    }

    private static string Rel(string path) => Path.GetRelativePath(SourceTree.Root, path);

    private static string Join(IEnumerable<string> labels) =>
        string.Join(", ", labels.OrderBy(l => l, StringComparer.Ordinal));

    private static string Bullets(IEnumerable<string> items) =>
        string.Join(Environment.NewLine, items.Select(i => $"  * {i}"));
}
