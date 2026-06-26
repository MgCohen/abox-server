using ABox.Domain.Agents;

namespace ABox.Agents.Tests.Support;

// No-CLI provider for tests: returns the config's scripted Reply, or echoes the prompt. Drives an
// agent through a flow without a live Claude/Codex process.
internal sealed class FakeProvider(AgentConfig config) : IProvider
{
    public Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct)
    {
        var text = (config as FakeAgentConfig)?.Reply ?? $"[{config.Name}] {request.Prompt}";
        var transcript = new[] { new AgentTurn(AgentTurnKind.Text, text) };
        return Task.FromResult(new DriveResult(text, request.SessionId ?? "fake-session", 0, text, transcript));
    }
}
