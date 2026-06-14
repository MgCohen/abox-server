using ABox.Domain.Agents.Judging;

namespace ABox.Tests.Unit.Tests;

public class JudgeScoreTests
{
    private static CriterionResult R(string id, Verdict status) => new(id, status, string.Empty);

    [Rule("JudgeScore with every criterion passing → overallPass and score10 of 10")]
    [Fact]
    public void All_pass_scores_ten_and_passes()
    {
        var score = JudgeScore.From([R("a", Verdict.Pass), R("b", Verdict.Pass), R("c", Verdict.Pass)]);

        Assert.Equal(10, score.Score10);
        Assert.True(score.OverallPass);
        Assert.Equal(3, score.Passed);
    }

    [Rule("JudgeScore with a failing criterion → overallPass false")]
    [Fact]
    public void One_fail_blocks_overall_pass()
    {
        var score = JudgeScore.From([R("a", Verdict.Pass), R("b", Verdict.Pass), R("c", Verdict.Pass), R("d", Verdict.Fail)]);

        Assert.False(score.OverallPass);
        Assert.Equal(8, score.Score10);
        Assert.Equal(1, score.Failed);
    }

    [Rule("JudgeScore counts indeterminate against the total → lower score and no pass")]
    [Fact]
    public void Indeterminate_penalizes_score_and_blocks_pass()
    {
        var score = JudgeScore.From([R("a", Verdict.Pass), R("b", Verdict.Pass), R("c", Verdict.Indeterminate), R("d", Verdict.Indeterminate)]);

        Assert.Equal(5, score.Score10);
        Assert.False(score.OverallPass);
        Assert.Equal(2, score.Indeterminate);
    }
}
