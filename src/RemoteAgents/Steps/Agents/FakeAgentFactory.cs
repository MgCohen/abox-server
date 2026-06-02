namespace RemoteAgents.Steps.Agents;

// PROVISIONAL single-role factory — the real named roles (implementer,
// reviewer) arrive at L7 mapping role -> provider + configured options.
public sealed class FakeAgentFactory : IAgentFactory
{
    public Agent Create(string role, string name, string prompt, string? sessionId = null) =>
        new FakeAgent(name, role, prompt, sessionId);
}
