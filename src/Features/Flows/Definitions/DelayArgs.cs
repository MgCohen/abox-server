namespace RemoteAgents.Domain.Flow.Operations;

// PROVISIONAL stub args — retired with StubFlow at L10.
public sealed record DelayArgs(string Name, int DelayMs, string Result) : OperationArgs(Name);
