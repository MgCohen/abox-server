using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using RemoteAgents.Contracts;
using RemoteAgents.Flows;
using RemoteAgents.Host;
using RemoteAgents.Paths;
using RemoteAgents.Projects;

var builder = WebApplication.CreateBuilder(args);

// Transport is Tailscale-only; CORS is wide open so a separate-origin WASM bundle
// can call the Host. No app-layer auth (feature-map A8).
const string Cors = "open";
builder.Services.AddCors(o => o.AddPolicy(Cors, p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

// String enums on the wire (FlowPhase/StepStatus render as names).
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Engine services + the flow catalog. Flows are declared once in FlowManifest and
// walked here — no hand-picking, no metadata strings at the call site (ADR 0001).
builder.Services.AddSingleton<IOrchestratorPaths, OrchestratorPaths>();
builder.Services.AddSingleton<IProjectRegistry, ProjectRegistry>();
builder.Services.AddSingleton<IHistoryStore, FileHistoryStore>();
builder.Services.AddSingleton<FlowRegistry>();
builder.Services.AddSingleton<FlowCatalog>();
builder.Services.AddSingleton<IFlowFactory, FlowFactory>();
foreach (var definition in FlowManifest.Definitions)
{
    builder.Services.AddTransient(definition.FlowType);   // resolved per run, configured by the factory
    builder.Services.AddSingleton(definition);            // fed to FlowCatalog as IEnumerable<FlowDefinition>
}

var app = builder.Build();
app.UseCors(Cors);

// Fail-fast: building the catalog runs its boot guard (unique names, metadata, Flow types).
_ = app.Services.GetRequiredService<FlowCatalog>();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/projects", (IProjectRegistry projects) =>
    projects.List().Select(p => new ProjectInfo(p.Name, p.Path)));

app.MapGet("/catalog", (FlowCatalog catalog) =>
    catalog.All().Select(d => new FlowInfo(d.Config.Name, d.Config.Description)));

var flows = app.MapGroup("/flows");

flows.MapPost("/", (StartRunRequest req, IFlowFactory factory, FlowRegistry runs, IProjectRegistry projects) =>
{
    var flow = factory.Create(req.Flow);
    if (flow is null)
        return Results.NotFound(new { error = $"Unknown flow '{req.Flow}'." });

    string projectDir;
    try { projectDir = projects.Resolve(req.Project); }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }

    var id = runs.Start(flow, req.Project, projectDir, req.Prompt, req.Args ?? []);
    return Results.Ok(new StartRunResponse(id));
});

flows.MapGet("/", (FlowRegistry runs) => runs.List());

flows.MapGet("/{id:guid}", (Guid id, FlowRegistry runs, HttpContext http) =>
{
    var snap = runs.Get(id);
    if (snap is null) return Results.NotFound();

    var etag = $"\"{snap.Version}\"";
    if ((string?)http.Request.Headers.IfNoneMatch == etag)
        return Results.StatusCode(StatusCodes.Status304NotModified);

    http.Response.Headers.ETag = etag;
    return Results.Ok(snap);
});

flows.MapGet("/{id:guid}/events", (Guid id, FlowRegistry runs, HttpContext http, CancellationToken ct) =>
    Sse.Stream(http, runs.Changes(id, ct), ct));

flows.MapPost("/{id:guid}/cancel", (Guid id, FlowRegistry runs) =>
    runs.Cancel(id) ? Results.Accepted() : Results.NotFound());

app.Run();
