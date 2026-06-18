using System.Net;
using System.Net.Http.Json;
using ABox.Features.Decisions.Contracts;
using ABox.Tests.Wire.Support;

namespace ABox.Tests.Wire.Tests;

// Wire smoke for the Decisions slice over WebApplicationFactory. The repository-backed store is shared across
// the fixture, so each test scopes itself by its own created id / a unique tag rather than absolute counts.
[Collection(WireHostCollection.Name)]
public class DecisionsWireTests(WireApp app) : IClassFixture<WireApp>
{
    [Rule("POST /decisions → a created decision echoing the question and tags unanswered, rejecting a blank question")]
    [Fact]
    public async Task Post_creates_a_decision_and_it_round_trips()
    {
        var tag = $"raise-{Guid.NewGuid():N}";

        using var created = await app.CreateClient().PostAsJsonAsync(
            "/decisions", new RaiseDecisionRequest("Merge phase 3?", [tag]));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dto = await created.Content.ReadFromJsonAsync<DecisionView>();
        Assert.NotNull(dto);
        Assert.Equal("Merge phase 3?", dto!.Question);
        Assert.Equal([tag], dto.Tags);
        Assert.Null(dto.Answer);
        Assert.Null(dto.AnsweredAt);
        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Contains(dto.Id.ToString(), created.Headers.Location?.ToString());
    }

    [Rule("POST /decisions → a created decision echoing the question and tags unanswered, rejecting a blank question")]
    [Fact]
    public async Task Post_rejects_a_blank_question()
    {
        using var res = await app.CreateClient().PostAsJsonAsync("/decisions", new RaiseDecisionRequest("   ", []));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Rule("GET /decisions → the raised decisions as wire DTOs")]
    [Fact]
    public async Task Get_lists_the_raised_decision()
    {
        var client = app.CreateClient();
        var tag = $"list-{Guid.NewGuid():N}";
        using var created = await client.PostAsJsonAsync("/decisions", new RaiseDecisionRequest("mine?", [tag]));
        var minted = await created.Content.ReadFromJsonAsync<DecisionView>();

        var listed = await client.GetFromJsonAsync<DecisionView[]>("/decisions");

        Assert.NotNull(listed);
        Assert.Contains(listed!, d => d.Id == minted!.Id);
    }

    [Rule("GET /decisions/{id} → the decision, or 404 when absent")]
    [Fact]
    public async Task Get_by_id_returns_the_decision_without_answering_it()
    {
        var client = app.CreateClient();
        using var created = await client.PostAsJsonAsync("/decisions", new RaiseDecisionRequest("fetch me?", ["g"]));
        var dto = await created.Content.ReadFromJsonAsync<DecisionView>();

        using var res = await client.GetAsync($"/decisions/{dto!.Id}");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var fetched = await res.Content.ReadFromJsonAsync<DecisionView>();
        Assert.Equal(dto.Id, fetched!.Id);
        Assert.Null(fetched.AnsweredAt);
    }

    [Rule("GET /decisions/{id} → the decision, or 404 when absent")]
    [Fact]
    public async Task Get_by_id_returns_404_for_an_unknown_id()
    {
        using var res = await app.CreateClient().GetAsync($"/decisions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Rule("POST /decisions/{id}/answer → the decision stamped with its answer, or 404 when absent")]
    [Fact]
    public async Task Answer_records_the_yes_no_and_returns_the_updated_view()
    {
        var client = app.CreateClient();
        using var created = await client.PostAsJsonAsync("/decisions", new RaiseDecisionRequest("approve?", ["a"]));
        var dto = await created.Content.ReadFromJsonAsync<DecisionView>();
        Assert.Null(dto!.Answer);

        using var res = await client.PostAsJsonAsync(
            $"/decisions/{dto.Id}/answer", new AnswerDecisionRequest(Answer: true, Note: "ship it"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var answered = await res.Content.ReadFromJsonAsync<DecisionView>();
        Assert.True(answered!.Answer);
        Assert.Equal("ship it", answered.Note);
        Assert.NotNull(answered.AnsweredAt);
    }

    [Rule("POST /decisions/{id}/answer → the decision stamped with its answer, or 404 when absent")]
    [Fact]
    public async Task Answer_returns_404_for_an_unknown_id()
    {
        using var res = await app.CreateClient().PostAsJsonAsync(
            $"/decisions/{Guid.NewGuid()}/answer", new AnswerDecisionRequest(Answer: true, Note: null));

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
