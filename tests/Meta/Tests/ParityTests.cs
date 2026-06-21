namespace ABox.Tests.Meta.Tests;

// Parity for the whole repo, driven from outside the suites it checks: every product type's Rulebook and its
// [Rule]-cited tests stay in lockstep (reflecting over the product assembly via SuiteAnchor), and Meta's own
// Rulebook stays in lockstep with these tests. Data-driven over the taxonomy, so a new type is covered the
// moment it is registered.
public class ParityTests
{
    [Rule("Parity holds for every registered type")]
    [Fact]
    public void EveryTypeKeepsItsRulebookAndTestsInSync()
    {
        var product = typeof(SuiteAnchor).Assembly;
        foreach (var type in TestTypes.Registered)
            ParityGuard.For(product, type).Assert();

        ParityGuard.ForRulebook(
                typeof(ParityTests).Assembly,
                TestTypes.Namespace("Meta"),
                RepoTree.MetaRulesPath())
            .Assert();
    }
}
