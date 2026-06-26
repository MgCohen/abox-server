using System.Net;
using System.Net.Http.Json;
using ABox.Features.Inbox.Contracts;

namespace ABox.Host.Tests.Wire;

// Wire smoke for the Inbox slice over WebApplicationFactory. The repository-backed inbox is shared across the
// fixture, so each test scopes itself by a unique tag / its own created id rather than absolute counts.
[Collection(WireHostCollection.Name)]
public class InboxWireTests(WireApp app) : IClassFixture<WireApp>
{
    [Rule("POST /inbox → a created item echoing title and tags with timestamps null, rejecting a blank title")]
    [Fact]
    public async Task Post_creates_an_item_and_it_round_trips()
    {
        var tag = $"add-{Guid.NewGuid():N}";

        using var created = await app.CreateClient().PostAsJsonAsync(
            "/inbox", new AddNoteRequest("Phase 3 needs review", [tag]));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await created.Content.ReadFromJsonAsync<InboxItemView>();
        Assert.NotNull(dto);
        Assert.Equal("Phase 3 needs review", dto!.Title);
        Assert.Equal([tag], dto.Tags);
        Assert.Null(dto.SeenAt);
        Assert.Null(dto.CompletedAt);
        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Contains(dto.Id.ToString(), created.Headers.Location?.ToString());
    }

    [Rule("POST /inbox → a created item echoing title and tags with timestamps null, rejecting a blank title")]
    [Fact]
    public async Task Post_rejects_a_blank_title()
    {
        using var res = await app.CreateClient().PostAsJsonAsync("/inbox", new AddNoteRequest("   ", []));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Rule("GET /inbox → the inbox items as wire DTOs, filtered by tag")]
    [Fact]
    public async Task Get_filters_to_items_carrying_the_tag()
    {
        var client = app.CreateClient();
        var tag = $"filter-{Guid.NewGuid():N}";
        using var mine = await client.PostAsJsonAsync("/inbox", new AddNoteRequest("mine", [tag]));
        var minted = await mine.Content.ReadFromJsonAsync<InboxItemView>();
        await client.PostAsJsonAsync("/inbox", new AddNoteRequest("other", ["unrelated"]));

        var listed = await client.GetFromJsonAsync<InboxItemView[]>($"/inbox?tag={tag}");

        Assert.NotNull(listed);
        var only = Assert.Single(listed!);
        Assert.Equal(minted!.Id, only.Id);
    }

    [Rule("GET /inbox/{id} → the item, or 404 when absent")]
    [Fact]
    public async Task Get_by_id_returns_the_item_without_marking_it_seen()
    {
        var client = app.CreateClient();
        using var created = await client.PostAsJsonAsync("/inbox", new AddNoteRequest("fetch me", ["g"]));
        var dto = await created.Content.ReadFromJsonAsync<InboxItemView>();

        using var res = await client.GetAsync($"/inbox/{dto!.Id}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var fetched = await res.Content.ReadFromJsonAsync<InboxItemView>();
        Assert.Equal(dto.Id, fetched!.Id);
        Assert.Null(fetched.SeenAt);
    }

    [Rule("GET /inbox/{id} → the item, or 404 when absent")]
    [Fact]
    public async Task Get_by_id_returns_404_for_an_unknown_id()
    {
        using var res = await app.CreateClient().GetAsync($"/inbox/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Rule("POST /inbox/{id}/seen → the item stamped seen, or 404 when absent")]
    [Fact]
    public async Task Mark_seen_stamps_the_item()
    {
        var client = app.CreateClient();
        using var created = await client.PostAsJsonAsync("/inbox", new AddNoteRequest("see me", ["s"]));
        var dto = await created.Content.ReadFromJsonAsync<InboxItemView>();
        Assert.Null(dto!.SeenAt);

        using var res = await client.PostAsync($"/inbox/{dto.Id}/seen", null);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var seen = await res.Content.ReadFromJsonAsync<InboxItemView>();
        Assert.NotNull(seen!.SeenAt);
    }

    [Rule("POST /inbox/{id}/seen → the item stamped seen, or 404 when absent")]
    [Fact]
    public async Task Mark_seen_returns_404_for_an_unknown_id()
    {
        using var res = await app.CreateClient().PostAsync($"/inbox/{Guid.NewGuid()}/seen", null);

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Rule("POST /inbox/{id}/complete → the item stamped complete, or 404 when absent")]
    [Fact]
    public async Task Complete_stamps_the_item()
    {
        var client = app.CreateClient();
        using var created = await client.PostAsJsonAsync("/inbox", new AddNoteRequest("finish me", ["c"]));
        var dto = await created.Content.ReadFromJsonAsync<InboxItemView>();

        using var res = await client.PostAsync($"/inbox/{dto!.Id}/complete", null);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var done = await res.Content.ReadFromJsonAsync<InboxItemView>();
        Assert.NotNull(done!.CompletedAt);
    }

    [Rule("POST /inbox/{id}/complete → the item stamped complete, or 404 when absent")]
    [Fact]
    public async Task Complete_returns_404_for_an_unknown_id()
    {
        using var res = await app.CreateClient().PostAsync($"/inbox/{Guid.NewGuid()}/complete", null);

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
