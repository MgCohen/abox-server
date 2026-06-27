namespace ABox.Tests.Harness.Tests;

// Parity for the whole repo, driven from outside the suites it checks: every product type's Rulebook and its
// [Rule]-cited tests stay in lockstep, reflecting over the product assembly via SuiteAnchor. Data-driven over
// the taxonomy, so a new type is covered the moment it is registered. These harness tests are the enforcer,
// not part of the taxonomy they enforce — they carry no Rulebook of their own (no self-parity recursion).
public class ParityTests
{
    [Fact]
    public void EveryTypeKeepsItsRulebookAndTestsInSync()
    {
        var product = typeof(SuiteAnchor).Assembly;
        foreach (var type in RepoTree.TestTypeFolders().Where(TestTypes.IsRegistered))
            ParityGuard.For(product, type).Assert();
    }
}
