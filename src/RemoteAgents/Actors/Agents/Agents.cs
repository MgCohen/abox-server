using RemoteAgents.Actors.Agents.Codex;

namespace RemoteAgents.Actors.Agents;

public static class Agents
{
    public static readonly AgentConfig Implementer =
        new FakeAgentConfig("implementer", "Builds the change.", "fake-model", "You implement.");

    public static readonly AgentConfig Reviewer =
        new CodexConfig("reviewer", "Reviews the change.", "gpt-5.5", "You review.", Sandbox: "read-only");
}
