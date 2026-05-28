using RemoteAgents.Agents;
using RemoteAgents.Events;

namespace Flows.Agents;

// Static factory — flow files call Planner.Create(sink) and get a
// configured ClaudeAgent with the planning system prompt embedded in
// this assembly. Edit prompts/planner.md and rebuild to update.
public static class Planner
{
    public static ClaudeAgent Create(IEventSink? sink = null) => new()
    {
        Name = "planner",
        Sink = sink ?? NoOpSink.Instance,
        Options = new ClaudeAgentOptions(
            Model: "opus",
            SystemPrompt: Prompts.Load("planner")),
    };
}
