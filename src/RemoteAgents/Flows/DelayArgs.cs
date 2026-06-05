namespace RemoteAgents.Flows;

// PROVISIONAL stub args — retired with StubFlow at L10.
public sealed record DelayArgs(string Name, int DelayMs, string Result) : OperationArgs(Name);
