using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ABox.Features.Git.Contracts;

namespace ABox.Features.Git.PrList;

public static class PrListEndpoint
{
    public static void Map(IEndpointRouteBuilder prs) =>
        prs.MapGet("/", (IPullRequests pullRequests, string? project) =>
            Results.Ok(pullRequests.List(project ?? ".")));
}
