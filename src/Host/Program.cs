using ABox.Features.Flows.Module;
using ABox.Features.Git.Module;
using ABox.Features.Projects.Module;
using ABox.Features.Tasks.Module;
using ABox.Host;

var builder = WebApplication.CreateBuilder(args);
Composition.AddServices(builder);

var app = builder.Build();
app.UseCors(Composition.CorsPolicy);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapProjects();
app.MapFlows();
app.MapGit();
app.MapTasks();

app.Run();

// Exposed so the Wire test type can boot the real Host over WebApplicationFactory<Program>.
public partial class Program;
