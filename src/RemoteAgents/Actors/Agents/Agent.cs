using RemoteAgents.Flows;

namespace RemoteAgents.Actors.Agents;

public sealed class Agent(IProvider provider, string projectDir) : Operation<AgentArgs, AgentOutcome>
{
    private string? _sessionId;

    protected override async Task<AgentOutcome> Invoke(AgentArgs args, CancellationToken ct)
    {
        var request = new AgentRunRequest(args.Prompt, projectDir, _sessionId);
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
