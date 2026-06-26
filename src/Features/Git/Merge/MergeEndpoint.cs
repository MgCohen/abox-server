using FastEndpoints;
using ABox.Features.Git.Contract;

namespace ABox.Features.Git.Merge;

internal sealed class MergeEndpoint(IPullRequests pullRequests) : EndpointWithoutRequest<MergeResult>
{
    public override void Configure()
    {
        Post("/git/prs/{number}/merge");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var number = Route<int>("number");
        if (pullRequests.List(".").Any(p => p.Number == number))
        {
            await Send.OkAsync(new MergeResult(number, "merged"), ct);
            return;
        }

        // The 404 body is a custom {error} shape, not the endpoint's MergeResult, so it goes out the
        // arbitrary-object door rather than Send.ResponseAsync (which is typed to TResponse).
        await HttpContext.Response.SendAsync(new PrNotFound($"PR #{number} not found."), 404, cancellation: ct);
    }
}
