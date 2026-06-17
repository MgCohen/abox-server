using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ABox.Domain.Git;
using ABox.Features.Git.Contracts;
using ABox.Features.Git.PrList;

namespace ABox.Features.Git.Module;

public static class GitModule
{
    public static Assembly EndpointsAssembly => typeof(PrListEndpoint).Assembly;

    public static IServiceCollection AddGit(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddSingleton<IPullRequests, StubPullRequests>();

        var gitHub = ReadGitHubOptions(configuration);
        if (gitHub is not null)
        {
            services.AddSingleton(gitHub);
            services.AddHttpClient<IStackHost, GitHubStackHost>();
        }
        else
        {
            services.AddSingleton<IStackHost, InMemoryStackHost>();
        }

        return services;
    }

    private static GitHubOptions? ReadGitHubOptions(IConfiguration? configuration)
    {
        var token = configuration?.GetSection("GitHub")["Token"];
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var section = configuration!.GetSection("GitHub");
        return new GitHubOptions(section["Owner"] ?? "", section["Repo"] ?? "", token);
    }
}
