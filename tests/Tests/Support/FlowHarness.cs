using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using RemoteAgents.Domain.Agents;
using RemoteAgents.Domain.Flow;
using RemoteAgents.Host;

namespace RemoteAgents.Tests.Support;

// The API-down e2e backbone: boots the real composition and drives a flow end to end in-process. Steps,
// the Flow engine, the snapshot stream, and the resolver wiring are all real — only the agent's mouth (the
// provider) is swappable. With a scripted provider it is deterministic and CI-safe (E2E); with the default
// (live) provider it drives the real CLI (Live). Shared between both types, hence its home in Support.
internal static class FlowHarness
{
    public static async Task<FlowSnapshot> RunAsync(
        string flowId, string prompt, string projectDir, TimeSpan timeout, IProvider? provider = null)
    {
        var builder = WebApplication.CreateBuilder();
        Composition.AddServices(builder, catalog =>
        {
            catalog.Register<StubFlow>(new FlowConfig("stub", "Walking-skeleton stub: placeholder steps, no real work."));
            catalog.Register<CodexPingFlow>(new FlowConfig("codex-ping", "Drive the Codex reviewer with the run prompt."));
            catalog.Register<ClaudePingFlow>(new FlowConfig("claude-ping", "Drive the Claude implementer with the run prompt."));
        });

        // A scripted provider swaps the agent's mouth for determinism; the real factory (live CLI) stays the
        // default. Registered last so it wins over Composition's AgentFactory.
        if (provider is not null)
            builder.Services.AddSingleton<IAgentFactory>(sp =>
                new ScriptedAgentFactory(provider, sp.GetRequiredService<ResolverSelector>()));

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
