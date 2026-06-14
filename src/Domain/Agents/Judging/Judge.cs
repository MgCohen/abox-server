using ABox.Infrastructure.Operations;

namespace ABox.Domain.Agents.Judging;

public sealed class Judge(IProvider provider, string projectDir) : Operation<JudgeArgs, JudgeVerdict>
{
    protected override async Task<JudgeVerdict> Invoke(JudgeArgs args, CancellationToken ct)
    {
        var request = args.Request;
        var prompt = JudgePrompt.Compose(request);
        var drive = await provider.DriveAsync(new AgentRunRequest(prompt, projectDir), ct).ConfigureAwait(false);

        var results = drive.ExitCode == 0
            ? JudgeParser.Parse(drive.Text, request.Criteria)
            : Faulted(request.Criteria, FaultReason(drive));

        return new JudgeVerdict(request.Subject, results, JudgeScore.From(results));
    }

    private static IReadOnlyList<CriterionResult> Faulted(IReadOnlyList<Criterion> criteria, string reason)
        => criteria.Select(c => new CriterionResult(c.Id, Verdict.Indeterminate, reason)).ToList();

    private static string FaultReason(DriveResult drive)
        => string.IsNullOrWhiteSpace(drive.Text) ? "judge provider exited non-zero" : drive.Text.Trim();
}
