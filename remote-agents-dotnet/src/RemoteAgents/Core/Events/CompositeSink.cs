namespace RemoteAgents.Events;

// Fan-out wrapper. Emits to each child sequentially in registration order
// so transcripts stay deterministic. Any child throwing aborts the rest.
public sealed class CompositeSink : IEventSink
{
    private readonly IReadOnlyList<IEventSink> _sinks;

    public CompositeSink(params IEventSink[] sinks) => _sinks = sinks;

    public async Task EmitAsync(AgentEvent evt, CancellationToken ct = default)
    {
        foreach (var s in _sinks) await s.EmitAsync(evt, ct);
    }
}
