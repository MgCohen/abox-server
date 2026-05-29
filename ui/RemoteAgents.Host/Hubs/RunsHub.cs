using Microsoft.AspNetCore.SignalR;
using RemoteAgents.Events;
using RemoteAgents.Host.Runs;

namespace RemoteAgents.Host.Hubs;

// SignalR hub at /hub/runs. Clients call Stream(runId) and receive a
// server-to-client stream of AgentEvents until the run completes or
// the client disconnects.
//
// One stream per run — the chat-content variants (AssistantText, UserText,
// Thinking, ToolUse, ToolResult, Meta) ride the same channel as the
// orchestration events (Started, Phase, ProviderSessionAttached, …).
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
}
