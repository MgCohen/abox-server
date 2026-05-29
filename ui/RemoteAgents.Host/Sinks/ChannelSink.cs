using System.Threading.Channels;
using RemoteAgents.Events;

namespace RemoteAgents.Host.Sinks;

// IEventSink backed by a bounded Channel<AgentEvent>. EmitAsync writes to
// the writer; SignalR (or any consumer) tails the reader. Bounded so a
// disconnected client can't grow unbounded memory; FullMode=Wait gives
// natural backpressure to the emitter — fine for the Host's case where
// the emitter is a JSONL-tailing loop, not the agent itself.
public sealed class ChannelSink : IEventSink, IAsyncDisposable
{
    private readonly Channel<AgentEvent> _channel;

    public ChannelSink(int capacity = 1024)
    {
        _channel = Channel.CreateBounded<AgentEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    public ChannelReader<AgentEvent> Reader => _channel.Reader;

    public Task EmitAsync(AgentEvent evt, CancellationToken ct = default) =>
        _channel.Writer.WriteAsync(evt, ct).AsTask();

    // Signal end-of-stream so subscribers' `await foreach` loops complete.
    public void Complete() => _channel.Writer.TryComplete();

    public ValueTask DisposeAsync()
    {
        Complete();
        return ValueTask.CompletedTask;
    }
}
