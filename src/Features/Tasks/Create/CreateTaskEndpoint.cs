using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ABox.Features.Git.Contract;
using ABox.Features.Tasks.Contract;

namespace ABox.Features.Tasks.Create;

// Mode 2 (cross-feature, decoupled): a Task links the open PRs it finds, reading them ONLY through Git's
// published IPullRequests contract — never Git's implementation. The impl is supplied at runtime by DI.
public static class CreateTaskEndpoint
{
    public static void Map(IEndpointRouteBuilder tasks) =>
        tasks.MapPost("/", (CreateTaskRequest req, IPullRequests pullRequests) =>
        {
            var openPullRequests = pullRequests.List(".")
                .Where(pr => pr.State == "open")
                .Select(pr => pr.Number)
                .ToList();
            return Results.Ok(new TaskDto(1, req.Title, openPullRequests));
        });
}
