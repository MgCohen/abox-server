using ABox.Tests.Harness;

namespace ABox.Agents.Tests;

// This feature owns its parity across all three types it holds: each type's co-located Rulebook and the
// [Rule]-cited tests beside it stay in lockstep, scoped to ABox.Agents.Tests.<Type>. Meta sweeps every
// assembly too; this is the fast local signal.
public class Parity
{
    [Fact]
    public void RulebooksAndTestsAgree()
    {
        foreach (var type in new[] { "Unit", "E2E", "Live" })
            ParityGuard.ForColocated(typeof(Parity).Assembly, type).Assert();
    }
}
