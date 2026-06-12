namespace RemoteAgents.Tests.Wire.Tests;

// Guards the link between the Wire Rulebook and its [Rule] tests, via the shared ParityGuard engine.
// Cardinality 1:N (not strict); requireAllCited — every test here carries a Rule.
public class ParityTests
{
    [ParityFact]
    public void Rulebook_and_tests_are_in_sync() =>
        ParityGuard.For(typeof(ParityTests)).Assert("Wire/Rulebook/rules.md", requireAllCited: true);
}
