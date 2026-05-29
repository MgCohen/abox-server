using Microsoft.AspNetCore.SignalR;
using RemoteAgents.Events;
using RemoteAgents.Host.Runs;
using System.Threading.Channels;

namespace RemoteAgents.Host.Hubs;

// SignalR hub at /hub/runs. Clients call Stream(runId) and receive a
// server-to-client stream of AgentEvents until the run completes (the
// ChannelSink is marked Complete) or the client disconnects.
//
// AgentEvent is polymorphic via [JsonPolymorphic] attributes on the base
// record — SignalR's default JsonHubProtocol uses System.Text.Json under
// the hood, so the "kind" discriminator round-trips with no extra setup.
public sealed class RunsHub : Hub
{
    private readonly RunRegistry _registry;
    private readonly ILogger<RunsHub> _log;

    public RunsHub(RunRegistry registry, ILogger<RunsHub> log)
    {
        _registry = registry;
        _log = log;
    }

    // Server-to-client streaming method. SignalR auto-pumps the
    // ChannelReader<T> to the calling client until it completes.
    public ChannelReader<AgentEvent> Stream(Guid runId, CancellationToken ct)
    {
        var run = _registry.Get(runId)
            ?? throw new HubException($"Run {runId} not found.");

        _log.LogInformation("Client {ConnId} subscribing to run {RunId}", Context.ConnectionId, runId);
        return run.Sink.Reader;
    }

    // Structured chat-event stream, sourced from Claude's per-session
    // JSONL at ~/.claude/projects/<encoded-cwd>/<session-id>.jsonl.
    // Same lifetime as Stream above: completes when ChatChannel.Complete
    // is called (FlowRunner does this in its finally block).
    public ChannelReader<ChatEvent> StreamChat(Guid runId, CancellationToken ct)
    {
        var run = _registry.Get(runId)
            ?? throw new HubException($"Run {runId} not found.");

        _log.LogInformation("Client {ConnId} subscribing to chat stream for {RunId}", Context.ConnectionId, runId);
        return run.Chat.Reader;
    }
}
