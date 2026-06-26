namespace ABox.Features.Decisions.Contract;

public sealed record AnswerDecisionRequest(bool? Answer, string? Note);
