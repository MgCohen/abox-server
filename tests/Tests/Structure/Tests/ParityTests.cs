namespace RemoteAgents.Tests.Structure.Tests;

// Guards the link between the Structure Rulebook and the enforcing tests, via the shared ParityGuard
// engine. Strict 1:1 — a placement invariant is one Rule, one sweeping assertion.
public class ParityTests
{
    [Fact]
    public void Rulebook_and_tests_are_in_sync() =>
        ParityGuard.For(typeof(ParityTests), strict: true).Assert("Structure/Rulebook/rules.md");
}
