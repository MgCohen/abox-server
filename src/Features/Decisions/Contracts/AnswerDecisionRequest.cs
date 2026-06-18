namespace ABox.Features.Decisions.Contracts;

public sealed record AnswerDecisionRequest(bool? Answer, string? Note);
