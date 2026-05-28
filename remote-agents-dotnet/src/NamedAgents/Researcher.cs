using RemoteAgents.Agents;
using RemoteAgents.Events;

namespace Flows.Agents;

public static class Researcher
{
    public static CodexAgent Create(IEventSink? sink = null) => new()
    {
        Name = "researcher",
        Sink = sink ?? NoOpSink.Instance,
        Options = new CodexAgentOptions(
            Model: "gpt-5.5",
            SystemPrompt: Prompts.Load("researcher")),
    };
}
