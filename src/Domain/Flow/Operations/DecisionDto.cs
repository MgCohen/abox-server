namespace ABox.Domain.Flow.Operations;

public sealed record DecisionDto(
    string Kind,
    string Prompt,
    string Answer,
    string Source,
    DateTimeOffset At);
