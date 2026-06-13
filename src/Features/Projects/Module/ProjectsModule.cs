using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ABox.Features.Projects.List;

namespace ABox.Features.Projects.Module;

public static class ProjectsModule
{
    public static IServiceCollection AddProjects(this IServiceCollection services)
    {
        services.AddHostedService<ProjectSeeder>();
        return services;
    }

    public static void MapProjects(this IEndpointRouteBuilder app)
    {
        var projects = app.MapGroup("/projects");
        ListProjectsEndpoint.Map(projects);
    }
}
