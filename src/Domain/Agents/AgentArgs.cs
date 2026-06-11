using RemoteAgents.Infrastructure.Operations;

namespace RemoteAgents.Domain.Agents;

public sealed record AgentArgs(string Intent, string Prompt) : OperationArgs(Intent);
