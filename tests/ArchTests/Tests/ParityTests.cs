namespace RemoteAgents.Tests.ArchTests;

// Guards the link between the rule book and the enforcing tests, via the shared ParityGuard engine. A
// block with no test would pass vacuously (a documented rule no one checks); a test citing a missing block
// would drift from the spec. Strict 1:1 — an architecture invariant is one Rule, one sweeping assertion.
public class ParityTests
{
    [Fact]
    public void Rulebook_and_tests_are_in_sync() =>
        ParityGuard.For(typeof(ParityTests), strict: true).Assert("Fixtures/rules.md");
}
