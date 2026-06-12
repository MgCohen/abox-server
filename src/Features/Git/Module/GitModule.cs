using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ABox.Features.Git.Contracts;
using ABox.Features.Git.PrList;
using ABox.Features.Git.PrOps;

namespace ABox.Features.Git.Module;

public static class GitModule
{
    public static IServiceCollection AddGit(this IServiceCollection services)
    {
        services.AddSingleton<IPullRequests, StubPullRequests>();
        return services;
    }

    public static void MapGit(this IEndpointRouteBuilder app)
    {
        var prs = app.MapGroup("/git/prs");
        PrListEndpoint.Map(prs);
        PrMergeEndpoint.Map(prs);
    }
}
