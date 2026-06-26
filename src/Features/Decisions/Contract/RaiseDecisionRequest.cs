namespace ABox.Features.Decisions.Contract;

public sealed record RaiseDecisionRequest(string? Question, IReadOnlyList<string>? Tags);
