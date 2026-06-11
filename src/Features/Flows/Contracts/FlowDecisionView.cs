namespace RemoteAgents.Features.Flows.Contracts;

public sealed record FlowDecisionView(
    string Kind,
    string Prompt,
    string Answer,
    string Source,
    DateTimeOffset At);
