using System.Threading.Channels;
using RemoteAgents.Host.Hubs;

namespace RemoteAgents.Host.Sinks;

// Bounded Channel<ChatEvent> per run. The JSONL tailer writes; SignalR
// hub subscribers read via StreamChat(runId). Bounded capacity gives
// natural backpressure if a client falls behind; FullMode=Wait blocks
// the tailer briefly rather than dropping events.
public sealed class ChatChannel : IAsyncDisposable
{
    private readonly Channel<ChatEvent> _channel;

    public ChatChannel(int capacity = 512)
    {
        _channel = Channel.CreateBounded<ChatEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true,
        });
    }

    public ChannelReader<ChatEvent> Reader => _channel.Reader;
    public ChannelWriter<ChatEvent> Writer => _channel.Writer;

    public Task EmitAsync(ChatEvent evt, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(evt, ct).AsTask();

    public void Complete() => _channel.Writer.TryComplete();

    public ValueTask DisposeAsync()
    {
        Complete();
        return ValueTask.CompletedTask;
    }
}
