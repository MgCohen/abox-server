using System.Net.Http.Json;
using RemoteAgents.Flows;
using RemoteAgents.Wire;

namespace RemoteAgents.UI.Components.Api;

// Typed wrapper around RemoteAgents.Host's REST surface. Workstream B
// switched the wire shape from RunRecord+SignalR to FlowSnapshot+SSE
// (D1–D4). The catalog of available flow definitions lives at /catalog;
// the runtime registry (active + recent runs) at /flows.
public sealed class HostApiClient
{
    private readonly HttpClient _http;

    public HostApiClient(HttpClient http) => _http = http;

    public Task<ProjectInfo[]?> GetProjectsAsync(CancellationToken ct = default) =>
        _http.GetFromJsonAsync<ProjectInfo[]>("projects", ct);

    public Task<FlowInfo[]?> GetCatalogAsync(CancellationToken ct = default) =>
        _http.GetFromJsonAsync<FlowInfo[]>("catalog", ct);

    public Task<FlowSnapshot[]?> GetFlowsAsync(CancellationToken ct = default) =>
        _http.GetFromJsonAsync<FlowSnapshot[]>("flows", ct);

    public Task<FlowSnapshot?> GetFlowAsync(Guid id, CancellationToken ct = default) =>
        _http.GetFromJsonAsync<FlowSnapshot>($"flows/{id}", ct);

    public async Task<FlowSnapshot?> StartFlowAsync(StartRunRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("flows", req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<FlowSnapshot>(cancellationToken: ct);
    }

    public Task CancelFlowAsync(Guid id, CancellationToken ct = default) =>
        _http.PostAsync($"flows/{id}/cancel", content: null, ct);

    public Task AnswerFlowAsync(Guid id, string choice, CancellationToken ct = default) =>
        _http.PostAsJsonAsync($"flows/{id}/answer", new RespondRequest(null, choice), ct);
}
