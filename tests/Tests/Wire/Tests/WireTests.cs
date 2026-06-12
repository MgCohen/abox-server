using System.Net;
using System.Net.Http.Json;
using RemoteAgents.Features.Flows.Contracts;
using RemoteAgents.Tests.Wire.Support;

namespace RemoteAgents.Tests.Wire.Tests;

// Wire smoke: a real HttpClient against the Host over WebApplicationFactory. Thin by design — it proves
// routing + serialization + the SSE streaming contract, with a CLI-free StubFlow behind the flow endpoints.
public class WireTests(WireApp app) : IClassFixture<WireApp>
{
    [Rule("health returns ok")]
    public async Task Health_returns_ok()
    {
        using var res = await app.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("ok", await res.Content.ReadAsStringAsync());
    }

    [Rule("projects lists the registered projects")]
    public async Task Projects_lists_the_registered_projects()
    {
        using var res = await app.CreateClient().GetAsync("/projects");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("demo", await res.Content.ReadAsStringAsync());
    }

    [Rule("a started flow streams snapshots over SSE to completion")]
    public async Task Started_flow_streams_snapshots_over_sse_to_completion()
    {
        var client = app.CreateClient();

        using var started = await client.PostAsJsonAsync("/flows", new StartRunRequest("demo", "stub", "do the thing"));
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        var run = await started.Content.ReadFromJsonAsync<StartRunResponse>();
        Assert.NotNull(run);

        var events = await client.GetStringAsync($"/flows/{run!.Id}/events");
        Assert.Contains(run.Id.ToString(), events);
        Assert.Contains("Completed", events);
    }
}
