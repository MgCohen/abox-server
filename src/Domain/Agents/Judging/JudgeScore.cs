namespace ABox.Domain.Agents.Judging;

public sealed record JudgeScore(int Passed, int Failed, int Indeterminate, int Total, int Score10, bool OverallPass)
{
    public static JudgeScore From(IReadOnlyList<CriterionResult> results)
    {
        var passed = results.Count(r => r.Status == Verdict.Pass);
        var failed = results.Count(r => r.Status == Verdict.Fail);
        var indeterminate = results.Count(r => r.Status == Verdict.Indeterminate);
        var total = results.Count;
        var score10 = total == 0 ? 0 : (int)Math.Round(10.0 * passed / total, MidpointRounding.AwayFromZero);
        return new JudgeScore(passed, failed, indeterminate, total, score10, failed == 0 && indeterminate == 0);
    }
}
