namespace RemoteAgents.Actors.Agents;

// PROVISIONAL single-role factory — the real named roles (implementer,
// reviewer) arrive at L7 mapping role -> provider + configured options.
public sealed class FakeAgentFactory : IAgentFactory
{
    public Agent Create(string role) => new FakeAgent(role);
}
