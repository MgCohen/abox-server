using ABox.Tests.Harness;

namespace ABox.Git.Tests;

// This feature owns its parity across both the types it holds: each type's co-located Rulebook and the
// [Rule]-cited tests beside it stay in lockstep, scoped to ABox.Git.Tests.<Type>. Meta sweeps every assembly
// too; this is the fast local signal.
public class Parity
{
    [Fact]
    public void RulebooksAndTestsAgree()
    {
        foreach (var type in new[] { "Unit", "E2E" })
            ParityGuard.ForColocated(typeof(Parity).Assembly, type).Assert();
    }
}
