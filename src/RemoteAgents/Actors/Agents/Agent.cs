using RemoteAgents.Flows;

namespace RemoteAgents.Actors.Agents;

public sealed class Agent(AgentConfig config, IProvider provider)
{
    public string Name => config.Name;

    public IOperation<AgentResult> Run(string prompt, string? sessionId = null) =>
        new RunOperation(config.Name, provider, prompt, sessionId);

    private sealed class RunOperation(string name, IProvider provider, string prompt, string? sessionId)
        : IOperation<AgentResult>
    {
        public string Name => name;

        public async Task<AgentResult> Execute(FlowContext ctx, CancellationToken ct)
        {
            var request = new AgentRunRequest(prompt, ctx.ProjectDir, sessionId);
            var drive = await provider.DriveAsync(request, ct).ConfigureAwait(false);
            return new AgentResult(drive.Text, drive.SessionId, drive.ExitCode, drive.RawOutput, drive.Transcript);
        }
    }
}
