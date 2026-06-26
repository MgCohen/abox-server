using ABox.Tests.Harness;

namespace ABox.Decisions.Tests;

// This feature owns its parity: each type's co-located Rulebook and the [Rule]-cited tests beside it stay in
// lockstep, scoped to ABox.Decisions.Tests.<Type>. Meta sweeps every assembly too; this is the fast signal.
public class Parity
{
    [Fact]
    public void RulebooksAndTestsAgree()
    {
        foreach (var type in new[] { "Unit" })
            ParityGuard.ForColocated(typeof(Parity).Assembly, type).Assert();
    }
}
