using RemoteAgents.Primitives;

namespace RemoteAgents.Tests.Primitives;

// We don't shell out to a real `gh` here — it'd need a GitHub repo +
// auth. Validation tests are enough; live exercise happens in flows.
public class GhOpsTests
{
    [Fact]
    public async Task PrCreateAsync_empty_title_throws()
    {
        var req = new GhPrCreateRequest(ProjectDir: ".", Title: "", Body: "body");
        await Assert.ThrowsAsync<ArgumentException>(() => GhOps.PrCreateAsync(req));
    }

    [Fact]
    public async Task PrCreateAsync_empty_body_throws()
    {
        var req = new GhPrCreateRequest(ProjectDir: ".", Title: "title", Body: "");
        await Assert.ThrowsAsync<ArgumentException>(() => GhOps.PrCreateAsync(req));
    }

    [Fact]
    public async Task PrCommentAsync_empty_body_throws()
    {
        var req = new GhPrCommentRequest(ProjectDir: ".", Selector: "feature/x", Body: "");
        await Assert.ThrowsAsync<ArgumentException>(() => GhOps.PrCommentAsync(req));
    }
}
