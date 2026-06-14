namespace ABox.Domain.Agents.Judging;

public sealed record CriterionResult(string CriterionId, Verdict Status, string Evidence);
