namespace RemoteAgents.Contracts;

public sealed record DecisionDto(
    string Kind,
    string Prompt,
    string Answer,
    string Source,
    DateTimeOffset At);
