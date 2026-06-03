using RemoteAgents.Actors.Agents;
using RemoteAgents.Contracts;
using RemoteAgents.Flows;
using Xunit.Abstractions;

namespace RemoteAgents.Tests;

public class CodexSmokeTests(ITestOutputHelper output)
{
    private sealed class OneShotFlow(IAgentFactory agents, AgentConfig config, string prompt) : Flow
    {
        protected override Task RunAsync(FlowConfig flowConfig, FlowContext ctx, CancellationToken ct) =>
            Run(agents.Create(config).Run(prompt), ct);
    }

    [Fact(Skip = "integration: needs codex CLI + ChatGPT subscription; remove Skip to run manually")]
    public async Task A_flow_drives_the_real_codex_reviewer_end_to_end()
    {
        var projectDir = Directory.CreateTempSubdirectory("codex-smoke-").FullName;
        try
        {
            var flow = new OneShotFlow(new AgentFactory(), Agents.Reviewer, "Reply with the single word: PONG");
            var ctx = new FlowContext("codex-smoke", "smoke", projectDir, "seed");
            var stream = new SnapshotStream(flow, ctx);

            await flow.ExecuteAsync(new FlowConfig("codex-smoke", "smoke"), ctx, CancellationToken.None);

            var op = stream.Latest.Operations.Single();
            output.WriteLine($"Phase={stream.Latest.Phase} Op={op.Name} Status={op.Status}");
            output.WriteLine($"Summary={op.Summary}");

            Assert.Equal(FlowPhase.Completed, stream.Latest.Phase);
            Assert.Equal(OperationStatus.Completed, op.Status);
            Assert.Equal("reviewer", op.Name);
            Assert.False(string.IsNullOrWhiteSpace(op.Summary));
        }
        finally { try { Directory.Delete(projectDir, recursive: true); } catch { /* best-effort */ } }
    }
}
