namespace RemoteAgents.Actors.Agents;

public interface IProvider
{
    Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct);
}
