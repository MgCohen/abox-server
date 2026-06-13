namespace ABox.Tests.E2E.Tests;

// Guards the link between the E2E Rulebook and its [Rule] tests, via the shared ParityGuard engine.
// requireAllCited — every test here carries a Rule (1:N: a guarantee may have several case tests).
public class ParityTests
{
    [ParityFact]
    public void Rulebook_and_tests_are_in_sync() =>
        ParityGuard.For(typeof(ParityTests)).Assert(requireAllCited: true);
}
