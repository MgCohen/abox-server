using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ABox.Domain.Projects;
using ABox.Features.Flows.Contracts;
using ABox.Features.Projects.Contracts;
using ABox.Infrastructure.Storage;
using ABox.Tests.Wire.Support;

namespace ABox.Tests.Wire.Tests;

// Wire smoke: a real HttpClient against the Host over WebApplicationFactory. Thin by design — it proves
// routing + serialization + the SSE streaming contract, with a CLI-free StubFlow behind the flow endpoints.
[Collection(WireHostCollection.Name)]
public class WireTests(WireApp app) : IClassFixture<WireApp>
{
    [Rule("health returns ok")]
    [Fact]
    public async Task Health_returns_ok()
    {
        using var res = await app.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("ok", await res.Content.ReadAsStringAsync());
    }

    [Rule("GET /projects lists the stored projects as wire DTOs")]
    [Fact]
    public async Task Projects_lists_the_stored_projects()
    {
        var store = app.Services.GetRequiredService<IRepository<Project>>();
        await store.Add(Project.Create("Listed Project", app.ProjectDir));

        using var res = await app.CreateClient().GetAsync("/projects");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var projects = await res.Content.ReadFromJsonAsync<ProjectDto[]>();
        Assert.NotNull(projects);
        var stored = await store.GetAll();
        Assert.Equal(
            stored.Select(p => (p.Id, p.Name, p.Path)).OrderBy(p => p.Name),
            projects!.Select(p => (p.Id, p.Name, p.Path)).OrderBy(p => p.Name));
    }

    [Rule("GET /projects/{id} returns the project, or 404 when absent")]
    [Fact]
    public async Task Get_by_id_returns_the_stored_project()
    {
        var stored = Project.Create("Fetchable Project", app.ProjectDir);
        await app.Services.GetRequiredService<IRepository<Project>>().Add(stored);

        using var res = await app.CreateClient().GetAsync($"/projects/{stored.Id}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.Equal((stored.Id, stored.Name, stored.Path), (dto!.Id, dto.Name, dto.Path));
    }

    [Rule("GET /projects/{id} returns the project, or 404 when absent")]
    [Fact]
    public async Task Get_by_id_returns_404_for_an_unknown_id()
    {
        using var res = await app.CreateClient().GetAsync($"/projects/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Rule("POST /projects creates a project, rejecting blank name, blank path, and duplicate names")]
    [Fact]
    public async Task Post_creates_a_project_and_it_round_trips()
    {
        var client = app.CreateClient();

        using var created = await client.PostAsJsonAsync(
            "/projects", new CreateProjectRequest("  Fresh Project  ", app.ProjectDir));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await created.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.NotNull(dto);
        Assert.Equal("Fresh Project", dto!.Name);
        Assert.Equal(app.ProjectDir, dto.Path);
        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Contains(dto.Id.ToString(), created.Headers.Location?.ToString());

        using var fetched = await client.GetAsync($"/projects/{dto.Id}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        var round = await fetched.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.Equal((dto.Id, dto.Name, dto.Path), (round!.Id, round.Name, round.Path));
    }

    [Rule("POST /projects creates a project, rejecting blank name, blank path, and duplicate names")]
    [Fact]
    public async Task Post_rejects_a_blank_name()
    {
        using var res = await app.CreateClient().PostAsJsonAsync(
            "/projects", new CreateProjectRequest("   ", app.ProjectDir));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Rule("POST /projects creates a project, rejecting blank name, blank path, and duplicate names")]
    [Fact]
    public async Task Post_rejects_a_blank_path()
    {
        using var res = await app.CreateClient().PostAsJsonAsync(
            "/projects", new CreateProjectRequest("Pathless Project", "   "));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Rule("POST /projects creates a project, rejecting blank name, blank path, and duplicate names")]
    [Fact]
    public async Task Post_rejects_a_duplicate_name()
    {
        var existing = Project.Create("Existing Project", app.ProjectDir);
        await app.Services.GetRequiredService<IRepository<Project>>().Add(existing);

        using var res = await app.CreateClient().PostAsJsonAsync(
            "/projects", new CreateProjectRequest(existing.Name, app.ProjectDir));

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Rule("PUT /projects/{id} updates a project, rejecting unknown id, blank fields, and duplicate names")]
    [Fact]
    public async Task Put_updates_name_and_path_and_round_trips()
    {
        var client = app.CreateClient();
        var store = app.Services.GetRequiredService<IRepository<Project>>();
        var original = Project.Create("Before Rename", app.ProjectDir);
        await store.Add(original);

        using var res = await client.PutAsJsonAsync(
            $"/projects/{original.Id}", new UpdateProjectRequest(original.Id, "After Rename", app.StorageDir));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.Equal((original.Id, "After Rename", app.StorageDir), (dto!.Id, dto.Name, dto.Path));

        using var fetched = await client.GetAsync($"/projects/{original.Id}");
        var round = await fetched.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.Equal((dto.Id, dto.Name, dto.Path), (round!.Id, round.Name, round.Path));
    }

    [Rule("PUT /projects/{id} updates a project, rejecting unknown id, blank fields, and duplicate names")]
    [Fact]
    public async Task Put_returns_404_for_an_unknown_id()
    {
        var id = Guid.NewGuid();
        using var res = await app.CreateClient().PutAsJsonAsync(
            $"/projects/{id}", new UpdateProjectRequest(id, "Ghost", app.ProjectDir));

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Rule("PUT /projects/{id} updates a project, rejecting unknown id, blank fields, and duplicate names")]
    [Fact]
    public async Task Put_rejects_a_blank_name()
    {
        var store = app.Services.GetRequiredService<IRepository<Project>>();
        var p = Project.Create("Has A Name", app.ProjectDir);
        await store.Add(p);

        using var res = await app.CreateClient().PutAsJsonAsync(
            $"/projects/{p.Id}", new UpdateProjectRequest(p.Id, "   ", app.ProjectDir));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Rule("PUT /projects/{id} updates a project, rejecting unknown id, blank fields, and duplicate names")]
    [Fact]
    public async Task Put_rejects_renaming_onto_another_projects_name()
    {
        var store = app.Services.GetRequiredService<IRepository<Project>>();
        var alpha = Project.Create("Alpha Unique", app.ProjectDir);
        var beta = Project.Create("Beta Unique", app.ProjectDir);
        await store.Add(alpha);
        await store.Add(beta);

        using var res = await app.CreateClient().PutAsJsonAsync(
            $"/projects/{beta.Id}", new UpdateProjectRequest(beta.Id, alpha.Name, app.ProjectDir));

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Rule("DELETE /projects/{id} removes a project, or 404 when absent")]
    [Fact]
    public async Task Delete_removes_the_project()
    {
        var client = app.CreateClient();
        var store = app.Services.GetRequiredService<IRepository<Project>>();
        var p = Project.Create("Deletable Project", app.ProjectDir);
        await store.Add(p);

        using var res = await client.DeleteAsync($"/projects/{p.Id}");
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        using var fetched = await client.GetAsync($"/projects/{p.Id}");
        Assert.Equal(HttpStatusCode.NotFound, fetched.StatusCode);
    }

    [Rule("DELETE /projects/{id} removes a project, or 404 when absent")]
    [Fact]
    public async Task Delete_returns_404_for_an_unknown_id()
    {
        using var res = await app.CreateClient().DeleteAsync($"/projects/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Rule("a started flow streams snapshots over SSE to completion")]
    [Fact]
    public async Task Started_flow_streams_snapshots_over_sse_to_completion()
    {
        var client = app.CreateClient();
        await app.Services.GetRequiredService<IRepository<Project>>()
            .Add(Project.Create("demo", app.ProjectDir));

        using var started = await client.PostAsJsonAsync("/flows", new StartRunRequest("demo", "stub", "do the thing"));
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        var run = await started.Content.ReadFromJsonAsync<StartRunResponse>();
        Assert.NotNull(run);

        var events = await client.GetStringAsync($"/flows/{run!.Id}/events");
        Assert.Contains(run.Id.ToString(), events);
        Assert.Contains("Completed", events);
    }
}
