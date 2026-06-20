using System.Text.Json.Serialization;
using FastEndpoints;
using ABox.Features.Flows.Module;
using ABox.Features.Tasks.Module;
using ABox.Host;

var builder = WebApplication.CreateBuilder(args);
Composition.AddServices(builder);

var app = builder.Build();
app.UseCors(Composition.CorsPolicy);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.UseFastEndpoints(c => c.Serializer.Options.Converters.Add(new JsonStringEnumConverter()));
app.MapFlows();
app.MapTasks();

app.Run();

// Exposed so the Wire test type can boot the real Host over WebApplicationFactory<Program>.
public partial class Program;
