using ABox.Domain.Agents.Judging;

namespace ABox.Tests.Unit.Tests;

public class JudgeParserTests
{
    private static readonly IReadOnlyList<Criterion> TwoCriteria = [new("a", "first"), new("b", "second")];

    [Rule("JudgeParser given a verdict envelope → one result per criterion by id")]
    [Fact]
    public void Parses_one_result_per_criterion()
    {
        var text = "analysis here\n<<JUDGE_VERDICT>>\n{ \"results\": [ "
                 + "{ \"criterionId\": \"a\", \"status\": \"pass\", \"evidence\": \"x\" }, "
                 + "{ \"criterionId\": \"b\", \"status\": \"fail\", \"evidence\": \"y\" } ] }";

        var results = JudgeParser.Parse(text, TwoCriteria);

        Assert.Equal(["a", "b"], results.Select(r => r.CriterionId));
        Assert.Equal(Verdict.Pass, results[0].Status);
        Assert.Equal(Verdict.Fail, results[1].Status);
    }

    [Rule("JudgeParser with a criterion absent from the envelope → marks it indeterminate")]
    [Fact]
    public void Missing_criterion_is_indeterminate()
    {
        var text = "<<JUDGE_VERDICT>>\n{ \"results\": [ { \"criterionId\": \"a\", \"status\": \"pass\", \"evidence\": \"x\" } ] }";

        var results = JudgeParser.Parse(text, TwoCriteria);

        Assert.Equal(Verdict.Pass, results[0].Status);
        Assert.Equal(Verdict.Indeterminate, results[1].Status);
    }

    [Rule("JudgeParser with no sentinel → marks every criterion indeterminate")]
    [Fact]
    public void No_sentinel_yields_all_indeterminate()
    {
        var results = JudgeParser.Parse("just prose, no verdict envelope", TwoCriteria);

        Assert.All(results, r => Assert.Equal(Verdict.Indeterminate, r.Status));
    }
}
