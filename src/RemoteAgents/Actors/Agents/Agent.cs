using RemoteAgents.Flows;

namespace RemoteAgents.Actors.Agents;

public abstract class Agent(string name)
{
    public string Name => name;

    public IOperation<AgentResult> Run(string prompt, string? sessionId = null) =>
        new RunOperation(this, prompt, sessionId);

    protected abstract Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct);

    private sealed class RunOperation(Agent agent, string prompt, string? sessionId) : IOperation<AgentResult>
    {
        public string Name => agent.Name;

        public async Task<AgentResult> Execute(FlowContext ctx, CancellationToken ct)
        {
            var request = new AgentRunRequest(prompt, ctx.ProjectDir, sessionId);
            var drive = await agent.DriveAsync(request, ct).ConfigureAwait(false);
            return new AgentResult(drive.Text, drive.SessionId, drive.ExitCode, drive.RawOutput, drive.Transcript);
        }
    }
}
