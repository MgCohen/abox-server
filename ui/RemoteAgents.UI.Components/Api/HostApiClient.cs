using System.Net.Http.Json;
using RemoteAgents.Runs;
using RemoteAgents.Wire;

namespace RemoteAgents.UI.Components.Api;

// Typed wrapper around RemoteAgents.Host's REST surface. Injected via
// DI; one instance per Blazor app. The HttpClient's BaseAddress is set
// by the host shell (UI.Web Program.cs / UI.Maui MauiProgram.cs).
public sealed class HostApiClient
{
    private readonly HttpClient _http;

    public HostApiClient(HttpClient http) => _http = http;

    public Task<ProjectInfo[]?> GetProjectsAsync(CancellationToken ct = default) =>
        _http.GetFromJsonAsync<ProjectInfo[]>("projects", ct);

    // The catalog of available flow definitions ("what can I run").
    // /flows now hosts the runtime registry (active + recent runs);
    // catalog moved to /catalog in Workstream B.
    public Task<FlowInfo[]?> GetFlowsAsync(CancellationToken ct = default) =>
        _http.GetFromJsonAsync<FlowInfo[]>("catalog", ct);

    public Task<RunRecord[]?> GetRunsAsync(CancellationToken ct = default) =>
        _http.GetFromJsonAsync<RunRecord[]>("runs", ct);

    public Task<RunRecord?> GetRunAsync(Guid id, CancellationToken ct = default) =>
        _http.GetFromJsonAsync<RunRecord>($"runs/{id}", ct);

    public async Task<RunRecord?> StartRunAsync(StartRunRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("runs", req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<RunRecord>(cancellationToken: ct);
    }

    public Task CancelRunAsync(Guid id, CancellationToken ct = default) =>
        _http.PostAsync($"runs/{id}/cancel", content: null, ct);

    public Task RespondAsync(Guid id, RespondRequest req, CancellationToken ct = default) =>
        _http.PostAsJsonAsync($"runs/{id}/respond", req, ct);

    public async Task<string?> GetOutputAsync(Guid id, CancellationToken ct = default)
    {
        var resp = await _http.GetAsync($"runs/{id}/output", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadAsStringAsync(ct);
    }
}
