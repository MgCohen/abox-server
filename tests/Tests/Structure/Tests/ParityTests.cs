namespace ABox.Tests.Structure.Tests;

// Guards the link between the Structure Rulebook and the enforcing tests, via the shared ParityGuard engine.
// requireAllCited — every test in this type carries a [Rule]; the Rulebook is the complete invariant set.
public class ParityTests
{
    [ParityFact]
    public void Rulebook_and_tests_are_in_sync() =>
        ParityGuard.For(typeof(ParityTests)).Assert(requireAllCited: true);
}
