namespace RemoteAgents.Steps.Agents;

// PROVISIONAL canned agent for the walking skeleton — replaced by the real
// providers (Claude/Codex) at L6.
public sealed class FakeAgent(string name, string role, string prompt, string? sessionId)
    : Agent(name, prompt, sessionId)
{
    protected override Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct)
    {
        var text = $"[{role}] {request.Prompt}";
        var transcript = new[] { new AgentTurn(AgentTurnKind.Text, text) };
        return Task.FromResult(new DriveResult(text, request.SessionId ?? "fake-session", 0, text, transcript));
    }
}
