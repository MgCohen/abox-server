using ABox.Domain.Flow;

namespace ABox.Tests.E2E.Tests;

// In-process E2E: drives the claude-ping flow through the real composition with a scripted provider, so the
// whole spine below the HTTP layer runs deterministically without a live CLI. The Live type covers the same
// flow against the real agent.
public class PingFlowTests
{
    [Rule("claude-ping drives the implementer to completion with the scripted reply")]
    public async Task Claude_ping_completes_with_the_scripted_reply()
    {
        var projectDir = Directory.CreateTempSubdirectory("ping-e2e-").FullName;
        try
        {
            var snap = await FlowHarness.RunAsync(
                "claude-ping", "Reply with the single word: PONG",
                projectDir, TimeSpan.FromSeconds(10),
                provider: new ScriptedProvider("PONG"));

            Assert.Equal(FlowPhase.Completed, snap.Phase);
            Assert.Contains("PONG", snap.Operations.Single().Summary);
        }
        finally
        {
            try { Directory.Delete(projectDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}
