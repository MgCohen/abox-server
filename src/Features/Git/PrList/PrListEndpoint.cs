using FastEndpoints;
using ABox.Features.Git.Contract;

namespace ABox.Features.Git.PrList;

internal sealed class PrListEndpoint(IPullRequests pullRequests) : EndpointWithoutRequest<IReadOnlyList<PullRequestDto>>
{
    public override void Configure()
    {
        Get("/git/prs");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var project = Query<string?>("project", isRequired: false) ?? ".";
        await Send.OkAsync(pullRequests.List(project), ct);
    }
}
