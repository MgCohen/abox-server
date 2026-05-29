using RemoteAgents.Events;

namespace RemoteAgents.Host.Sinks;

// IEventSink that fans out to N concurrent SignalR subscribers via
// Broadcaster<AgentEvent>, with bounded byte-sized replay so a late
// joiner (or browser refresh / mobile reconnect) gets the run history
// before the live tail.
//
// Sizing: StreamChunk.Chunk.Length (UTF-16 char count, conservative
// proxy for byte size); all other AgentEvents = 0 so they're never
// evicted — they're tiny and the UI wants them.
public sealed class ChannelSink : IEventSink, IAsyncDisposable
{
    private readonly Broadcaster<AgentEvent> _broadcaster;

    public ChannelSink(long maxReplayBytes = 1_048_576)
    {
        _broadcaster = new Broadcaster<AgentEvent>(SizeOf, maxReplayBytes);
    }

    public Broadcaster<AgentEvent> Broadcaster => _broadcaster;

    public Task EmitAsync(AgentEvent evt, CancellationToken ct = default) =>
        _broadcaster.EmitAsync(evt, ct);

    public void Complete() => _broadcaster.Complete();

    public ValueTask DisposeAsync() => _broadcaster.DisposeAsync();

    private static int SizeOf(AgentEvent evt) => evt switch
    {
        AgentEvent.StreamChunk c => c.Chunk.Length,
        _                        => 0,
    };
}
