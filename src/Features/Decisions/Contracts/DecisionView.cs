namespace ABox.Features.Decisions.Contracts;

public sealed record DecisionView(
    Guid Id,
    string Question,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    bool? Answer,
    string? Note,
    DateTimeOffset? AnsweredAt);
