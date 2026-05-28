namespace RemoteAgents.Events;

// Default sink — discards everything. Lets Agent.RunAsync work without
// requiring callers to attach a sink for trivial cases / unit tests.
public sealed class NoOpSink : IEventSink
{
    public static readonly NoOpSink Instance = new();
    public Task EmitAsync(AgentEvent evt, CancellationToken ct = default) => Task.CompletedTask;
}
