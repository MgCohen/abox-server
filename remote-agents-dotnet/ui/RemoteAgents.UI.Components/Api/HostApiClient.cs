using System.Net.Http.Json;
using RemoteAgents.UI.Components.Models;

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

    public Task<FlowInfo[]?> GetFlowsAsync(CancellationToken ct = default) =>
        _http.GetFromJsonAsync<FlowInfo[]>("flows", ct);

    public Task<RunSummary[]?> GetRunsAsync(CancellationToken ct = default) =>
        _http.GetFromJsonAsync<RunSummary[]>("runs", ct);

    public Task<RunSummary?> GetRunAsync(Guid id, CancellationToken ct = default) =>
        _http.GetFromJsonAsync<RunSummary>($"runs/{id}", ct);

    public async Task<RunSummary?> StartRunAsync(StartRunRequest req, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("runs", req, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<RunSummary>(cancellationToken: ct);
    }

    public Task CancelRunAsync(Guid id, CancellationToken ct = default) =>
        _http.PostAsync($"runs/{id}/cancel", content: null, ct);

    public Task RespondAsync(Guid id, RespondRequest req, CancellationToken ct = default) =>
        _http.PostAsJsonAsync($"runs/{id}/respond", req, ct);
}
