using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using RemoteAgents.Contracts;
using RemoteAgents.Engine.Flows;
using RemoteAgents.Host;

namespace RemoteAgents.Tests;

internal static class LiveSmoke
{
    public static async Task<FlowSnapshot> RunAsync(string flowId, string prompt, string projectDir, TimeSpan timeout)
    {
        var builder = WebApplication.CreateBuilder();
        Composition.AddServices(builder);
        await using var app = builder.Build();

        var launcher = app.Services.GetRequiredService<FlowLauncher>();
        var registry = app.Services.GetRequiredService<FlowRegistry>();

        using var cts = new CancellationTokenSource(timeout);
        var id = launcher.Start(flowId, "smoke", projectDir, prompt)
            ?? throw new InvalidOperationException($"flow '{flowId}' did not start");

        FlowSnapshot? last = null;
        await foreach (var snap in registry.Changes(id, cts.Token))
            last = snap;

        return last ?? throw new InvalidOperationException($"flow '{flowId}' produced no snapshot");
    }
}
