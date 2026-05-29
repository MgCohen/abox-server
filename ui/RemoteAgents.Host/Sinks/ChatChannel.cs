using RemoteAgents.Chat;

namespace RemoteAgents.Host.Sinks;

// Per-run multi-cast ChatEvent stream with replay. Same shape as
// ChannelSink; sized by event count rather than bytes (chat events
// are small structured blobs, not raw PTY chunks). Cap at 4096 to
// cover even a chatty run while preventing unbounded growth.
public sealed class ChatChannel : IAsyncDisposable
{
    private readonly Broadcaster<ChatEvent> _broadcaster;

    public ChatChannel(int capacity = 4096)
    {
        // Use sizeOf=1 per event so eviction is count-based: cap evicts
        // the oldest event whenever total exceeds `capacity`.
        _broadcaster = new Broadcaster<ChatEvent>(_ => 1, capacity);
    }

    public Broadcaster<ChatEvent> Broadcaster => _broadcaster;

    public Task EmitAsync(ChatEvent evt, CancellationToken ct = default) =>
        _broadcaster.EmitAsync(evt, ct);

    public void Complete() => _broadcaster.Complete();

    public ValueTask DisposeAsync() => _broadcaster.DisposeAsync();
}
