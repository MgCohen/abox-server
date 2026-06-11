using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using RemoteAgents.Features.Tasks.Create;

namespace RemoteAgents.Features.Tasks.Module;

public static class TasksModule
{
    public static void MapTasks(this IEndpointRouteBuilder app)
    {
        var tasks = app.MapGroup("/tasks");
        CreateTaskEndpoint.Map(tasks);
    }
}
