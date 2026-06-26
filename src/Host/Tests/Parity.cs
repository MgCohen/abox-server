using ABox.Tests.Harness;

namespace ABox.Host.Tests;

// The Host owns the wire-integration suite: its co-located Wire Rulebook and the [Rule]-cited tests beside it
// stay in lockstep, scoped to ABox.Host.Tests.<Type>. Kept a single assembly so the Host-boot collection
// serializes within it (no cross-assembly race). Meta sweeps every assembly too; this is the fast local signal.
public class Parity
{
    [Fact]
    public void RulebooksAndTestsAgree()
    {
        foreach (var type in new[] { "Wire" })
            ParityGuard.ForColocated(typeof(Parity).Assembly, type).Assert();
    }
}
