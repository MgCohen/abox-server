using RemoteAgents.Flows;

namespace RemoteAgents.Steps.Agents;

public abstract class Agent(string name, string prompt, string? sessionId) : IStepHandler<AgentResult>
{
    public string Name => name;

    public async Task<AgentResult> RunAsync(FlowContext ctx, CancellationToken ct)
    {
        var request = new AgentRunRequest(prompt, ctx.ProjectDir, sessionId);
        var drive = await DriveAsync(request, ct).ConfigureAwait(false);
        return new AgentResult(drive.Text, drive.SessionId, drive.ExitCode, drive.RawOutput, drive.Transcript);
    }

    protected abstract Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct);
}
