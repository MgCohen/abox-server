namespace ABox.Domain.Agents.Judging;

public sealed record JudgeRequest(
    string Subject,
    IReadOnlyList<Criterion> Criteria,
    string Context,
    IReadOnlyList<string> Files);
