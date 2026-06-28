using ABox.Tests.Central;

namespace ABox.Tests.Harness.Tests;

// Parity for the whole repo, driven from outside the suites it checks: every product type's Rulebook and its
// [Rule]-cited tests stay in lockstep, reflecting over the product assembly via SuiteAnchor. Data-driven over
// the taxonomy, so a new type is covered the moment it is registered. The harness then eats its own dog food —
// a self-Rulebook (tests/Harness/Tests/Rulebook.md) holds these enforcer tests to the same bar — while staying
// outside the product taxonomy (not a registered type, not a doc-engine instance).
public class ParityTests
{
    [Rule("Parity holds for every registered type and the harness's own tests")]
    [Fact]
    public void EveryTypeKeepsItsRulebookAndTestsInSync()
    {
        var product = typeof(SuiteAnchor).Assembly;
        foreach (var type in RepoTree.TestTypeFolders().Where(TestTypes.IsRegistered))
            ParityGuard.For(product, type).Assert();

        var self = typeof(ParityTests).Assembly;
        ParityGuard.ForRulebook(self, self.GetName().Name!,
            Path.Combine(RepoTree.HarnessTestsRoot, "Rulebook.md")).Assert();
    }
}
