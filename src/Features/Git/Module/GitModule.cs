using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ABox.Features.Git.Contracts;
using ABox.Features.Git.PrList;

namespace ABox.Features.Git.Module;

public static class GitModule
{
    public static Assembly EndpointsAssembly => typeof(PrListEndpoint).Assembly;

    public static IServiceCollection AddGit(this IServiceCollection services)
    {
        services.AddSingleton<IPullRequests, StubPullRequests>();
        return services;
    }
}
