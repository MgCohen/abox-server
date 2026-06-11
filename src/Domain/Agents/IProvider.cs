namespace RemoteAgents.Domain.Agents;

public interface IProvider
{
    Task<DriveResult> DriveAsync(AgentRunRequest request, CancellationToken ct);
}
