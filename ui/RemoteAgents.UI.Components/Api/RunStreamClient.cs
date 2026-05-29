using Microsoft.AspNetCore.SignalR.Client;
using RemoteAgents.Chat;
using RemoteAgents.Events;

namespace RemoteAgents.UI.Components.Api;

// SignalR client wrapper. One transient instance per RunView; the page
// constructs it with a hub URL + runId, calls StartAsync, then awaits
// the async-enumerable event stream.
//
// AgentEvent is the library record from RemoteAgents.Core.Events — the
// same one the Host emits — so the entire round-trip is statically
// typed without manual JSON code.
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

    // Structured Claude session-JSONL stream — typed assistant/tool/thinking
    // events rendered by the UI without going through terminal emulation.
    public IAsyncEnumerable<ChatEvent> StreamChatAsync(Guid runId, CancellationToken ct = default) =>
        _conn.StreamAsync<ChatEvent>("StreamChat", runId, ct);

    public ValueTask DisposeAsync() => _conn.DisposeAsync();
}
