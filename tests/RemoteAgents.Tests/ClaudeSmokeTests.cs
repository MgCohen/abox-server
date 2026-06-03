using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using RemoteAgents.Contracts;
using RemoteAgents.Flows;
using RemoteAgents.Host;
using Xunit.Abstractions;

namespace RemoteAgents.Tests;

public class ClaudeSmokeTests(ITestOutputHelper output)
{
    [Fact(Skip = "integration: needs claude CLI + Max subscription; remove Skip to run manually")]
    public async Task FlowLauncher_drives_the_registered_claude_flow_end_to_end()
    {
        var projectDir = Directory.CreateTempSubdirectory("claude-smoke-").FullName;
        var builder = WebApplication.CreateBuilder();
        Composition.AddServices(builder);
        await using var app = builder.Build();
        try
        {
            var launcher = app.Services.GetRequiredService<FlowLauncher>();
            var registry = app.Services.GetRequiredService<FlowRegistry>();

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var id = launcher.Start("claude-ping", "smoke", projectDir, "Reply with the single word: PONG");
            Assert.NotNull(id);

            FlowSnapshot? last = null;
            await foreach (var snap in registry.Changes(id!.Value, timeout.Token))
                last = snap;

            Assert.NotNull(last);
            var op = last!.Operations.Single();
            output.WriteLine($"Phase={last.Phase} Op={op.Name} Status={op.Status}");
            output.WriteLine($"Summary={op.Summary}");

            Assert.Equal(FlowPhase.Completed, last.Phase);
            Assert.Equal("implementer", op.Name);
            Assert.Equal(OperationStatus.Completed, op.Status);
            Assert.Contains("PONG", op.Summary ?? "");
        }
        finally { try { Directory.Delete(projectDir, recursive: true); } catch { /* best-effort */ } }
    }
}
