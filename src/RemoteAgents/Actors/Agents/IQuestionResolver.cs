namespace RemoteAgents.Actors.Agents;

public interface IQuestionResolver
{
    Task<string?> ResolveAsync(AgentQuestion question, CancellationToken ct);
}
