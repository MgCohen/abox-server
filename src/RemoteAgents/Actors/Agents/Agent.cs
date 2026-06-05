using RemoteAgents.Flows;

namespace RemoteAgents.Actors.Agents;

public sealed class Agent(IProvider provider, string projectDir) : Flow.Operation<AgentArgs, AgentResult>
{
    private string? _sessionId;

    protected override async Task<AgentResult> Invoke(AgentArgs args, CancellationToken ct)
    {
        _sessionId ??= Guid.NewGuid().ToString();
        var request = new AgentRunRequest(args.Prompt, projectDir, _sessionId);
        var drive = await provider.DriveAsync(request, ct).ConfigureAwait(false);
        _sessionId = drive.SessionId;
        return new AgentResult(drive.Text, drive.SessionId, drive.ExitCode, drive.RawOutput, drive.Transcript);
    }
}
