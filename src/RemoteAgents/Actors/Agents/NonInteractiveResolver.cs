namespace RemoteAgents.Actors.Agents;

// No human or config is available to answer, so a detected question is terminal:
// the flow surfaces NeedsInput and stops. Real auto-match / picker resolvers
// arrive with the interaction-modes + UI work.
public sealed class NonInteractiveResolver : IQuestionResolver
{
    public Task<string?> ResolveAsync(AgentQuestion question, CancellationToken ct)
        => Task.FromResult<string?>(null);
}
