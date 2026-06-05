using RemoteAgents.Flows;

namespace RemoteAgents.Actors.Agents;

public sealed record AgentArgs(string Intent, string Prompt) : OperationArgs(Intent);
