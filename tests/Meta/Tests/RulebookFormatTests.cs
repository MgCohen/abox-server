using static ABox.Tests.Harness.Report;

namespace ABox.Tests.Meta.Tests;

// The Rulebooks themselves stay well-formed: every Rule matches its type's template.md, each rules.md holds
// nothing but its Template/Harness pointers and Rules, and every template.md carries judge criteria. Read from
// disk (RepoTree + RulebookFormat), so a new type's Rulebook is covered the moment it lands.
public class RulebookFormatTests
{
    [Rule("Every Rule matches its type's template")]
    [Fact]
    public void EveryRuleMatchesItsTemplate()
    {
        var violations = new List<string>();
        foreach (var folder in RepoTree.RulebookFolders())
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
        foreach (var folder in RepoTree.RulebookFolders())
        {
            var rulesPath = Path.Combine(folder, "rules.md");
            var lines = File.ReadAllLines(rulesPath);

            foreach (var stray in RulebookFormat.Headings(lines).Where(h => h.Level != 3))
                violations.Add($"{Rel(rulesPath)} :: stray '{new string('#', stray.Level)} {stray.Text}' — " +
                    "rules.md holds only its Template/Harness pointers and '### ' Rules; context lives in template.md");

            var preamble = string.Join('\n', lines.TakeWhile(l => RulebookFormat.HeadingLevel(l) != 3));
            if (!preamble.Contains("template.md") || !preamble.Contains("Harness/README.md"))
                violations.Add($"{Rel(rulesPath)} :: must open with Template: and Harness: links before the Rules");
        }

        Assert.True(violations.Count == 0,
            $"""
            rules.md files drift from the shape (Template/Harness pointers, then '### ' Rules only):
            {Bullets(violations)}
            Keep rules.md to the Template:/Harness: links and '### ' Rules. All prose/context lives in template.md.
            """);
    }

    [Rule("Every template carries judge criteria")]
    [Fact]
    public void EveryTemplateCarriesCriteria()
    {
        var violations = new List<string>();
        foreach (var folder in RepoTree.RulebookFolders())
        {
            var templatePath = Path.Combine(folder, "template.md");
            if (RulebookFormat.Criteria(File.ReadAllLines(templatePath)).Count == 0)
                violations.Add($"{Rel(templatePath)} :: no '## Criteria' with '- **id:** …' bullets — " +
                    "/judge-rulebook has no rubric for this type");
        }

        Assert.True(violations.Count == 0,
            $"""
            template.md files are missing their judge criteria:
            {Bullets(violations)}
            Add a '## Criteria' section of '- **<id>:** <description>' bullets — the semantic rubric the judge grades against.
            """);
    }

    private static string Rel(string path) => Path.GetRelativePath(RepoTree.Root, path);
}
