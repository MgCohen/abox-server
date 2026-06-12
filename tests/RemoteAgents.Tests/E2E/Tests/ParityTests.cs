namespace RemoteAgents.Tests.E2E.Tests;

// Guards the link between the E2E Rulebook and its [Rule] tests, via the shared ParityGuard engine.
// Cardinality 1:N (not strict). The Rulebook accrues going-forward.
public class ParityTests
{
    [Fact]
    public void Rulebook_and_tests_are_in_sync() =>
        ParityGuard.For(typeof(ParityTests)).Assert("E2E/Rulebook/rules.md");
}
