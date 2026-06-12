using RemoteAgents.Features.Flows.Module;
using RemoteAgents.Features.Git.Module;
using RemoteAgents.Features.Tasks.Module;
using RemoteAgents.Host;
using RemoteAgents.Infrastructure.Projects;

var builder = WebApplication.CreateBuilder(args);
Composition.AddServices(builder);

var app = builder.Build();
app.UseCors(Composition.CorsPolicy);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/projects", (IProjectRegistry projects) => projects.List());

app.MapFlows();
app.MapGit();
app.MapTasks();

app.Run();

// Exposed so the Wire test type can boot the real Host over WebApplicationFactory<Program>.
public partial class Program;
