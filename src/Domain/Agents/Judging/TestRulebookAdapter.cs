namespace ABox.Domain.Agents.Judging;

public sealed class TestRulebookAdapter(Func<string, string> readText)
{
    private static readonly IReadOnlyList<Criterion> RubricCriteria =
    [
        new("cites_rule", "every [Fact] cites a [Rule(\"<exact header>\")]"),
        new("namespace", "namespace mirrors the folder path"),
        new("derived", "expected values are derived, not hardcoded"),
        new("faithful", "each method asserts what its name claims"),
    ];

    public JudgeRequest Adapt(string testPath)
    {
        var rulebookDir = RulebookDir(testPath);
        var rules = Path.Combine(rulebookDir, "rules.md");
        var template = Path.Combine(rulebookDir, "template.md");

        var context =
            $"## Test under review ({testPath})\n{readText(testPath)}\n\n" +
            $"## Rulebook — the standard it is graded against\n{readText(rules)}\n\n{readText(template)}";

        return new JudgeRequest("a unit test file vs its Rulebook", RubricCriteria, context, [testPath]);
    }

    public static string RulebookDir(string testPath)
    {
        var typeDir = Path.GetDirectoryName(Path.GetDirectoryName(testPath) ?? string.Empty) ?? string.Empty;
        return Path.Combine(typeDir, "Rulebook");
    }
}
