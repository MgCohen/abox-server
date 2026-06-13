namespace ABox.Tests.Live.Tests;

// Guards the link between the Live Rulebook and its [Rule] tests, via the shared ParityGuard engine.
// Default (not requireAllCited): the Rulebook accrues going-forward as smoke tests convert to [LiveFact].
public class ParityTests
{
    [ParityFact]
    public void Rulebook_and_tests_are_in_sync() =>
        ParityGuard.For(typeof(ParityTests)).Assert();
}
