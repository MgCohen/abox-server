using System.Net;
using System.Net.Http.Json;
using ABox.Features.Flows.Contracts;
using ABox.Features.Projects.Contracts;
using ABox.Tests.Wire.Support;

namespace ABox.Tests.Wire.Tests;

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

    [Rule("projects lists the seeded projects as wire DTOs")]
    public async Task Projects_lists_the_seeded_projects()
    {
        using var res = await app.CreateClient().GetAsync("/projects");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var projects = await res.Content.ReadFromJsonAsync<ProjectDto[]>();
        Assert.NotNull(projects);
        Assert.Contains(projects!, p => p.Name == "Card Framework");
        Assert.Contains(projects!, p => p.Name == "Scaffold");
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
