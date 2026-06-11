using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using RemoteAgents.Features.Git.Contracts;
using RemoteAgents.Features.Git.PrList;
using RemoteAgents.Features.Git.PrOps;

namespace RemoteAgents.Features.Git.Module;

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
