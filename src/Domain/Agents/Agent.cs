using ABox.Domain.Flow.Operations;
using ABox.Infrastructure.Operations;

namespace ABox.Domain.Agents;

public sealed class Agent(IProvider provider, IDecisionResolver resolver, int? resolveCap, string projectDir)
    : Operation<AgentArgs, AgentOutcome>, IDecisionSource
{
    private string? _sessionId;
    private readonly List<DecisionDto> _decisions = [];

    public IReadOnlyList<DecisionDto> Decisions => _decisions;

    protected override async Task<AgentOutcome> Invoke(AgentArgs args, CancellationToken ct)
    {
        _decisions.Clear();
        var outcome = await RunTurn(args.Prompt, ct).ConfigureAwait(false);

        var resolved = 0;
        while (outcome is AgentOutcome.NeedsInput needs)
        {
            if (resolveCap is { } cap && resolved >= cap)
                return new AgentOutcome.Faulted(needs.Result,
                    new AgentError(-1, $"auto-resolution exhausted after {cap} rounds"));

            var answer = await resolver.ResolveAsync(needs.Question, DecisionKind.Question, ct).ConfigureAwait(false);
            if (answer is null) return outcome;

            _decisions.Add(new DecisionDto(
                DecisionKind.Question.ToString(), needs.Question.Prompt, answer,
                resolver.Source.ToString(), DateTimeOffset.UtcNow));

            resolved++;
            outcome = await RunTurn(answer, ct).ConfigureAwait(false);
        }
        return outcome;
    }

    private async Task<AgentOutcome> RunTurn(string prompt, CancellationToken ct)
    {
        var request = new AgentRunRequest(prompt, projectDir, _sessionId);
        var drive = await provider.DriveAsync(request, ct).ConfigureAwait(false);
        _sessionId = drive.SessionId;
        var result = new AgentResult(drive.Text, drive.SessionId, drive.ExitCode, drive.RawOutput, drive.Transcript);

        // A broken executor emits a valid-looking question, so a fault takes
        // precedence over any parsed question (structured-questions FINDINGS Issue 1).
        if (drive.ExitCode != 0)
            return new AgentOutcome.Faulted(result, new AgentError(drive.ExitCode, FaultMessage(drive)));

        var question = QuestionParser.TryParse(drive.Text);
        return question is null
            ? new AgentOutcome.Completed(result)
            : new AgentOutcome.NeedsInput(result, question);
    }

    private static string FaultMessage(DriveResult drive)
        => string.IsNullOrWhiteSpace(drive.Text) ? "agent exited non-zero" : drive.Text.Trim();
}
