using System.Net;
using System.Net.Http.Json;
using ABox.Features.Git.Contracts;

namespace ABox.Host.Tests.Wire;

// Wire characterization of the Git PR endpoints over the real Host. Pins the StubPullRequests responses
// byte-for-byte so the canonical-shape port (Minimal API → FastEndpoints) cannot drift the HTTP contract.
[Collection(WireHostCollection.Name)]
public class GitPrsWireTests(WireApp app) : IClassFixture<WireApp>
{
    [Rule("GET /git/prs → the stub pull requests as wire DTOs")]
    [Fact]
    public async Task Lists_the_stub_pull_requests()
    {
        using var res = await app.CreateClient().GetAsync("/git/prs");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(
            """[{"number":101,"title":"Add health endpoint","state":"open"},{"number":102,"title":"Fix race in flow launcher","state":"open"},{"number":99,"title":"Bump dependencies","state":"merged"}]""",
            await res.Content.ReadAsStringAsync());
    }

    [Rule("POST /git/prs/{number}/merge → merged for a known PR, 404 for an unknown one")]
    [Fact]
    public async Task Merges_a_known_pull_request()
    {
        using var res = await app.CreateClient().PostAsync("/git/prs/101/merge", content: null);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("""{"number":101,"state":"merged"}""", await res.Content.ReadAsStringAsync());
    }

    [Rule("POST /git/prs/{number}/merge → merged for a known PR, 404 for an unknown one")]
    [Fact]
    public async Task Merge_returns_404_for_an_unknown_pull_request()
    {
        using var res = await app.CreateClient().PostAsync("/git/prs/12345/merge", content: null);

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("""{"error":"PR #12345 not found."}""", await res.Content.ReadAsStringAsync());
    }
}
