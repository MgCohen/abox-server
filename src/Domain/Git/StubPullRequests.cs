using ABox.Features.Git.Contract;

namespace ABox.Domain.Git;

// Provisional adapter: a fixed PR list standing in for a real GitHub/forge reader until that lands.
public sealed class StubPullRequests : IPullRequests
{
    public IReadOnlyList<PullRequestDto> List(string project) =>
    [
        new(101, "Add health endpoint", "open"),
        new(102, "Fix race in flow launcher", "open"),
        new(99, "Bump dependencies", "merged"),
    ];
}
