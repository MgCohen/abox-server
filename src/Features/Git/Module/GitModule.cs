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
        var section = configuration?.GetSection("GitHub");
        var token = section?["Token"];
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var owner = section!["Owner"];
        var repo = section["Repo"];
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            throw new InvalidOperationException(
                "GitHub:Token is set but GitHub:Owner and/or GitHub:Repo are missing. Set both, or unset the token to use the in-memory fake.");

        return new GitHubOptions(owner, repo, token);
    }
}
