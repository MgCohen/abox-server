namespace ABox.Tests.Unit.Tests;

// Guards the link between the Unit Rulebook and its [Rule] tests, via the shared ParityGuard engine.
// Cardinality 1:N (not strict) — a behavioral guarantee may be realized by several case tests. The
// Rulebook accrues going-forward, so it (and this guard) starts with whatever Rules have been authored.
public class ParityTests
{
    [ParityFact]
    public void Rulebook_and_tests_are_in_sync() =>
        ParityGuard.For(typeof(ParityTests)).Assert("Unit/Rulebook/rules.md");
}
