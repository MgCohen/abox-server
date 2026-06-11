namespace RemoteAgents.Tests.ArchTests;

// Guards the link between the rule book and the enforcing tests. A block with no test would pass
// vacuously (a documented rule no one checks); a test citing a missing/renamed block would drift
// from the spec. Either way this fails with the exact names to fix.
public class RuleBookTests
{
    [Fact]
    public void Every_rule_block_has_exactly_one_test()
    {
        var declared = RuleBook.DeclaredRules();
        var enforced = RuleBook.EnforcedRules();

        var unenforced = declared.Except(enforced).ToList();
        var undocumented = enforced.Except(declared).ToList();
        var duplicated = enforced.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        Assert.True(
            unenforced.Count == 0 && undocumented.Count == 0 && duplicated.Count == 0,
            $"""
            Rule book (Fixtures/rules.md) and rule tests are out of sync:
              blocks with no test:         {Fmt(unenforced)}
              tests citing a missing block:{Fmt(undocumented)}
              rules tested more than once: {Fmt(duplicated)}
            Fix: align each '### <name>' header with one [Rule("<name>")] test so the names match exactly.
            """);
    }

    private static string Fmt(IReadOnlyList<string> names) =>
        names.Count == 0 ? "(none)" : string.Join(", ", names.Select(n => $"\"{n}\""));
}
