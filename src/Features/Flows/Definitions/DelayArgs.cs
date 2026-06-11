using RemoteAgents.Domain.Flow.Operations;

namespace RemoteAgents.Features.Flows.Definitions;

// PROVISIONAL stub args — retired with StubFlow at L10.
public sealed record DelayArgs(string Name, int DelayMs, string Result) : OperationArgs(Name);
