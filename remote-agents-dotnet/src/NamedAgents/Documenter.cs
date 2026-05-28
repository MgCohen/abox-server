using RemoteAgents.Agents;
using RemoteAgents.Events;

namespace Flows.Agents;

public static class Documenter
{
    public static ClaudeAgent Create(IEventSink? sink = null) => new()
    {
        Name = "documenter",
        Sink = sink ?? NoOpSink.Instance,
        Options = new ClaudeAgentOptions(
            Model: "haiku",
            SystemPrompt: Prompts.Load("documenter")),
    };
}
