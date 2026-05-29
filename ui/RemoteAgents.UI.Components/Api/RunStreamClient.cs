using Microsoft.AspNetCore.SignalR.Client;
using RemoteAgents.Events;

namespace RemoteAgents.UI.Components.Api;

// SignalR client wrapper. One transient instance per RunView; the page
// constructs it with a hub URL + runId, calls StartAsync, then awaits
// the async-enumerable event stream.
//
// AgentEvent is the library record from RemoteAgents.Core.Events — the
// same one the Host emits — so the entire round-trip is statically
// typed without manual JSON code. Chat-content (AssistantText, UserText,
// Thinking, ToolUse, ToolResult, Meta) rides this same channel since
// the Phase 6 fold.
public sealed class RunStreamClient : IAsyncDisposable
{
    private readonly HubConnection _conn;

    public RunStreamClient(string hubUrl)
    {
        _conn = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();
    }

    public Task StartAsync(CancellationToken ct = default) => _conn.StartAsync(ct);

    public IAsyncEnumerable<AgentEvent> StreamAsync(Guid runId, CancellationToken ct = default) =>
        _conn.StreamAsync<AgentEvent>("Stream", runId, ct);

    public ValueTask DisposeAsync() => _conn.DisposeAsync();
}
