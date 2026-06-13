using ABox.Domain.Agents.Claude;
using ABox.Domain.Agents.Codex;

namespace ABox.Domain.Agents;

public static class Agents
{
    public static readonly AgentConfig Implementer =
        new ClaudeConfig("implementer", "Builds the change.", "", "You implement.", PermissionPolicy.Bypass);

    public static readonly AgentConfig Reviewer =
        new CodexConfig("reviewer", "Reviews the change.", "gpt-5.5", "You review.");
}
