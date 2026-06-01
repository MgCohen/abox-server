using System.Net;
using System.Net.Http.Json;
using RemoteAgents.Contracts;

namespace RemoteAgents.Web.Api;

/// <summary>Typed wrapper over the Host's REST surface: the flow catalog at
/// <c>/catalog</c>, registered projects at <c>/projects</c>, and the run
/// registry (active + recent) at <c>/flows</c>.</summary>
public sealed class HostApiClient(HttpClient http)
{
    public Task<ProjectInfo[]?> GetProjectsAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<ProjectInfo[]>("projects", WebJson.Options, ct);

    public Task<FlowInfo[]?> GetCatalogAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<FlowInfo[]>("catalog", WebJson.Options, ct);

    public Task<FlowSnapshot[]?> GetFlowsAsync(CancellationToken ct = default) =>
        http.GetFromJsonAsync<FlowSnapshot[]>("flows", WebJson.Options, ct);

    public async Task<FlowSnapshot?> GetFlowAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await http.GetAsync($"flows/{id}", ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<FlowSnapshot>(WebJson.Options, ct);
    }

    public async Task<Guid> StartFlowAsync(StartRunRequest req, CancellationToken ct = default)
    {
        var resp = await http.PostAsJsonAsync("flows", req, WebJson.Options, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<StartRunResponse>(WebJson.Options, ct);
        return body?.Id ?? throw new InvalidOperationException("Host returned no run id.");
    }

    public Task CancelFlowAsync(Guid id, CancellationToken ct = default) =>
        http.PostAsync($"flows/{id}/cancel", content: null, ct);
}
