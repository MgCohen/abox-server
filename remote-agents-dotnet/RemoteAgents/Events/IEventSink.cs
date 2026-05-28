namespace RemoteAgents.Events;

// The only logging surface. Built-in sinks: JsonlSink, ConsoleSink,
// CompositeSink, ProviderJsonlIngestSink (T0). Future UI seam: ChannelSink
// over System.Threading.Channels<AgentEvent>.
public interface IEventSink
{
    Task EmitAsync(AgentEvent evt, CancellationToken ct = default);
}
