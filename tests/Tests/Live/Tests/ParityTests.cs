namespace ABox.Tests.Live.Tests;

// Guards the link between the Live Rulebook and its [Rule] tests, via the shared ParityGuard engine.
// Cardinality 1:N (not strict). The Rulebook accrues going-forward.
public class ParityTests
{
    [ParityFact]
    public void Rulebook_and_tests_are_in_sync() =>
        ParityGuard.For(typeof(ParityTests)).Assert("Live/Rulebook/rules.md");
}
