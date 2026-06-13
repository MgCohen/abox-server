namespace ABox.Tests.Unit.Tests;

// Guards the link between the Unit Rulebook and its [Rule] tests, via the shared ParityGuard engine.
// Default (not requireAllCited): the Rulebook accrues going-forward, so existing [Fact]s may run uncited
// until backfilled, while every [Rule] still pairs 1:N with a real header.
public class ParityTests
{
    [ParityFact]
    public void Rulebook_and_tests_are_in_sync() =>
        ParityGuard.For(typeof(ParityTests)).Assert();
}
