using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using ABox.Features.Tasks.Create;

namespace ABox.Features.Tasks.Module;

public static class TasksModule
{
    public static void MapTasks(this IEndpointRouteBuilder app)
    {
        var tasks = app.MapGroup("/tasks");
        CreateTaskEndpoint.Map(tasks);
    }
}
