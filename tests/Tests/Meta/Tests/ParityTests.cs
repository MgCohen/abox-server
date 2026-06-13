namespace ABox.Tests.Meta.Tests;

// The one parity guard for the whole repo: for every registered type, its Rulebook headers and its
// [Rule]-cited tests stay in lockstep. Replaces the former per-type parity facts with a single data-driven
// check over the taxonomy, so a new type is covered the moment it is registered.
public class ParityTests
{
    [Rule("Parity holds for every registered type")]
    [Fact]
    public void EveryTypeKeepsItsRulebookAndTestsInSync()
    {
        foreach (var type in TestTypes.Registered)
            ParityGuard.For(typeof(ParityTests).Assembly, $"ABox.Tests.{type}.Tests")
                .Assert(requireAllCited: TestTypes.RequiresAllCited(type));
    }
}
