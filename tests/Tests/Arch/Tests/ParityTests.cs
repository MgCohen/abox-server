namespace ABox.Tests.Arch.Tests;

// Guards the link between the Arch Rulebook and the enforcing tests, via the shared ParityGuard engine.
// Strict 1:1 — an architecture invariant is one Rule, one sweeping assertion; a Rule with no test passes
// vacuously, a test citing a missing block drifts from the spec.
public class ParityTests
{
    [ParityFact]
    public void Rulebook_and_tests_are_in_sync() =>
        ParityGuard.For(typeof(ParityTests), strict: true).Assert("Arch/Rulebook/rules.md", requireAllCited: true);
}
