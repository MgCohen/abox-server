using ABox.Domain.Agents;
using ABox.Domain.Agents.Judging;

namespace ABox.Tests.Unit.Tests;

public class JudgeTests
{
    private sealed class StubProvider(string text, int exitCode = 0) : IProvider
    {
        public Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct)
            => Task.FromResult(new DriveResult(text, "session", exitCode, text, []));
    }

    private static JudgeRequest Request(IReadOnlyList<Criterion> criteria)
        => new("subject", criteria, "context blob", []);

    [Rule("Judge with a provider verdict envelope → scored verdict with per-criterion results")]
    [Fact]
    public async Task Produces_scored_verdict_from_provider_output()
    {
        var text = "<<JUDGE_VERDICT>>\n{ \"results\": [ "
                 + "{ \"criterionId\": \"a\", \"status\": \"pass\", \"evidence\": \"ok\" }, "
                 + "{ \"criterionId\": \"b\", \"status\": \"fail\", \"evidence\": \"bad\" } ] }";
        var judge = new Judge(new StubProvider(text), "C:/proj");

        var verdict = await Op.Exec(judge, new JudgeArgs(Request([new("a", "first"), new("b", "second")])));

        Assert.Equal(2, verdict.Results.Count);
        Assert.False(verdict.Score.OverallPass);
        Assert.Equal(5, verdict.Score.Score10);
    }

    [Rule("Judge with a provider fault → every criterion indeterminate")]
    [Fact]
    public async Task Provider_fault_yields_all_indeterminate()
    {
        var judge = new Judge(new StubProvider("boom", exitCode: 1), "C:/proj");

        var verdict = await Op.Exec(judge, new JudgeArgs(Request([new("a", "first")])));

        Assert.All(verdict.Results, r => Assert.Equal(Verdict.Indeterminate, r.Status));
        Assert.False(verdict.Score.OverallPass);
    }
}
