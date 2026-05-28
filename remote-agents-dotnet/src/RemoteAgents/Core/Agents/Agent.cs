using RemoteAgents.Events;

namespace RemoteAgents.Agents;

// Sealed lifecycle wrapper around provider-specific ExecuteAsync. Subclasses
// implement ExecuteAsync; this class owns Started/Completed/Failed emission
// and exception propagation. RunAsync is sealed so subclasses cannot weaken
// the contract — the only extension surface is ExecuteAsync + any virtual
// hooks the concrete subclass exposes.
public abstract class Agent
{
    public required string Name { get; init; }
    public IEventSink Sink { get; init; } = NoOpSink.Instance;

    public async Task<AgentResult> RunAsync(AgentRunRequest req, CancellationToken ct = default)
    {
        await Sink.EmitAsync(
            new AgentEvent.Started(DateTimeOffset.UtcNow, Name, req.Prompt, req.SessionId),
            ct);

        AgentResult result;
        try
        {
            result = await ExecuteAsync(req, ct);
        }
        catch (Exception ex)
        {
            // Always emit Failed — use CancellationToken.None so callers
            // get the failure event even when their token has been canceled.
            await Sink.EmitAsync(
                new AgentEvent.Failed(DateTimeOffset.UtcNow, Name, ex.Message, ex.GetType().Name),
                CancellationToken.None);
            throw;
        }

        await Sink.EmitAsync(
            new AgentEvent.Completed(
                DateTimeOffset.UtcNow,
                Name,
                result.SessionId,
                result.ExitCode,
                result.Text.Length),
            ct);

        return result;
    }

    protected abstract Task<AgentResult> ExecuteAsync(AgentRunRequest req, CancellationToken ct);
}
