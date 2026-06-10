using RemoteAgents.Features.Flows.Module;
using RemoteAgents.Host;
using RemoteAgents.Infrastructure.Projects;

var builder = WebApplication.CreateBuilder(args);
Composition.AddServices(builder);

var app = builder.Build();
app.UseCors(Composition.CorsPolicy);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/projects", (IProjectRegistry projects) => projects.List());

app.MapFlows();

app.Run();
