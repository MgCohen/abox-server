namespace ABox.Features.Decisions.Contracts;

public sealed record RaiseDecisionRequest(string? Question, IReadOnlyList<string>? Tags);
