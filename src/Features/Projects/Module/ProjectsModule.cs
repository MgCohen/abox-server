using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ABox.Features.Projects.List;

namespace ABox.Features.Projects.Module;

public static class ProjectsModule
{
    public static Assembly EndpointsAssembly => typeof(ListProjectsEndpoint).Assembly;

    public static IServiceCollection AddProjects(this IServiceCollection services)
    {
        services.AddHostedService<ProjectSeeder>();
        return services;
    }
}
