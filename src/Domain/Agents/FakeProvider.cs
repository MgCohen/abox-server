namespace RemoteAgents.Domain.Agents;

// PROVISIONAL fake drive for the walking skeleton — real providers (Codex/Claude)
// replace it as agents adopt them.
public sealed class FakeProvider(AgentConfig config) : IProvider
{
    public Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct)
    {
        var text = (config as FakeAgentConfig)?.Reply ?? $"[{config.Name}] {request.Prompt}";
        var transcript = new[] { new AgentTurn(AgentTurnKind.Text, text) };
        return Task.FromResult(new DriveResult(text, request.SessionId ?? "fake-session", 0, text, transcript));
    }
}
