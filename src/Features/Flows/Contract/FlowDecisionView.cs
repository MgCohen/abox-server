namespace ABox.Features.Flows.Contract;

public sealed record FlowDecisionView(
    string Kind,
    string Prompt,
    string Answer,
    string Source,
    DateTimeOffset At);
