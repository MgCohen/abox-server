using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ABox.Features.Git.Contracts;

namespace ABox.Features.Git.PrOps;

public static class PrMergeEndpoint
{
    public static void Map(IEndpointRouteBuilder prs) =>
        prs.MapPost("/{number:int}/merge", (int number, IPullRequests pullRequests) =>
        {
            var pr = pullRequests.List(".").FirstOrDefault(p => p.Number == number);
            return pr is null
                ? Results.NotFound(new { error = $"PR #{number} not found." })
                : Results.Ok(new MergeResult(number, "merged"));
        });
}
