using ABox.Tests.Harness;

namespace ABox.Inbox.Tests;

// This feature owns its parity: each type's co-located Rulebook and the [Rule]-cited tests beside it stay in
// lockstep, scoped to ABox.Inbox.Tests.<Type>. The Meta self-suite also sweeps every assembly (it is the
// repo-wide backstop), but a feature failing its own parity here gives the fast, local signal.
public class Parity
{
    [Fact]
    public void RulebooksAndTestsAgree()
    {
        foreach (var type in new[] { "Unit" })
            ParityGuard.ForColocated(typeof(Parity).Assembly, type).Assert();
    }
}
