namespace ABox.Domain.Agents.Judging;

public sealed record JudgeVerdict(string Subject, IReadOnlyList<CriterionResult> Results, JudgeScore Score)
{
    public override string ToString()
        => $"{(Score.OverallPass ? "PASS" : "FAIL")} {Score.Score10}/10 ({Score.Passed}/{Score.Total}) — {Subject}";
}
