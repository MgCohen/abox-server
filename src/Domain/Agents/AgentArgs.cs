using ABox.Infrastructure.Operations;

namespace ABox.Domain.Agents;

public sealed record AgentArgs(string Intent, string Prompt) : OperationArgs(Intent);
