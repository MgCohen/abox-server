using Microsoft.AspNetCore.SignalR;
using RemoteAgents.Chat;
using RemoteAgents.Events;
using RemoteAgents.Host.Runs;

namespace RemoteAgents.Host.Hubs;

// SignalR hub at /hub/runs. Clients call Stream(runId) and receive a
// server-to-client stream of AgentEvents until the run completes or
// the client disconnects.
//
// IAsyncEnumerable<T> (returned by Broadcaster.Subscribe) is wire-
// compatible with the previous ChannelReader<T> return — SignalR client
// code calling StreamAsync<T>("Stream", runId) doesn't care which the
// server hands back. The change to IAsyncEnumerable is what lets us
// replay the per-run history before tailing live events: a late-joining
// browser (refresh, second tab, mobile reconnect) catches up on the
// alt-screen boot bytes that the live channel had already discarded.
public sealed class RunsHub : Hub
{
    private readonly RunRegistry _registry;
    private readonly ILogger<RunsHub> _log;

    public RunsHub(RunRegistry registry, ILogger<RunsHub> log)
    {
        _registry = registry;
        _log = log;
    }

    public IAsyncEnumerable<AgentEvent> Stream(Guid runId, CancellationToken ct)
    {
        var run = _registry.Get(runId)
            ?? throw new HubException($"Run {runId} not found.");

        _log.LogInformation("Client {ConnId} subscribing to run {RunId}", Context.ConnectionId, runId);
        return run.Sink.Broadcaster.Subscribe(ct);
    }

    public IAsyncEnumerable<ChatEvent> StreamChat(Guid runId, CancellationToken ct)
    {
        var run = _registry.Get(runId)
            ?? throw new HubException($"Run {runId} not found.");

        _log.LogInformation("Client {ConnId} subscribing to chat stream for {RunId}", Context.ConnectionId, runId);
        return run.Chat.Broadcaster.Subscribe(ct);
    }
}
