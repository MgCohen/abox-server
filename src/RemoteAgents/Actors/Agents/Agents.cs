namespace RemoteAgents.Actors.Agents;

public static class Agents
{
    public static readonly AgentConfig Implementer =
        new FakeAgentConfig("implementer", "Builds the change.", "fake-model", "You implement.");

    public static readonly AgentConfig Reviewer =
        new FakeAgentConfig("reviewer", "Reviews the change.", "fake-model", "You review.");
}
