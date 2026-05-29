using RemoteAgents.Host;
using RemoteAgents.Host.Hubs;
using RemoteAgents.Host.Runs;
using RemoteAgents.Primitives;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<RunRegistry>();
builder.Services.AddSingleton<FlowRunner>();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

// CORS so a browser-served WASM bundle on a different origin can hit us.
// Tightened to specific origins in C3 once the deploy shape is fixed.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .SetIsOriginAllowed(_ => true)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();
app.UseCors();
app.MapHub<RunsHub>("/hub/runs");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// ---- Health -----------------------------------------------------------

app.MapGet("/health", () => Results.Ok(new { ok = true, at = DateTimeOffset.UtcNow }));

// ---- Projects ---------------------------------------------------------

app.MapGet("/projects", () =>
{
    var projects = ProjectRegistry.List()
        .Select(name => new ProjectInfo(name, SafeResolve(name)))
        .ToArray();
    return Results.Ok(projects);

    static string SafeResolve(string name)
    {
        try { return ProjectRegistry.Resolve(name); }
        catch { return ""; }
    }
});

// ---- Flows ------------------------------------------------------------

app.MapGet("/flows", (FlowRunner runner) =>
{
    var flowsDir = Path.Combine(runner.OrchestratorRoot, "cli", "flows");
    if (!Directory.Exists(flowsDir)) return Results.Ok(Array.Empty<FlowInfo>());

    var flows = Directory.EnumerateFiles(flowsDir, "*.cs", SearchOption.TopDirectoryOnly)
        .Select(path => Path.GetFileNameWithoutExtension(path)!)
        .Where(name => !name.StartsWith("smoke-"))
        .OrderBy(name => name)
        .Select(name => new FlowInfo(name, ReadFirstCommentBlock(Path.Combine(flowsDir, name + ".cs"))))
        .ToArray();
    return Results.Ok(flows);

    static string? ReadFirstCommentBlock(string path)
    {
        try
        {
            var lines = File.ReadLines(path).Take(20);
            var comments = lines
                .Where(l => l.TrimStart().StartsWith("//"))
                .Select(l => l.TrimStart().TrimStart('/').TrimStart())
                .Where(l => l.Length > 0)
                .Take(3)
                .ToArray();
            return comments.Length == 0 ? null : string.Join(" ", comments);
        }
        catch { return null; }
    }
});

// ---- Runs -------------------------------------------------------------

app.MapPost("/runs", (StartRunRequest req, FlowRunner runner) =>
{
    if (string.IsNullOrWhiteSpace(req.Project)) return Results.BadRequest(new { error = "project required" });
    if (string.IsNullOrWhiteSpace(req.Flow)) return Results.BadRequest(new { error = "flow required" });
    if (string.IsNullOrWhiteSpace(req.Prompt)) return Results.BadRequest(new { error = "prompt required" });

    var run = runner.Start(req.Project, req.Flow, req.Prompt, req.Args ?? []);
    return Results.Accepted($"/runs/{run.Id}", ToSummary(run));
});

app.MapGet("/runs", (RunRegistry registry) =>
{
    var summaries = registry.List().Select(ToSummary).ToArray();
    return Results.Ok(summaries);
});

app.MapGet("/runs/{id:guid}", (Guid id, RunRegistry registry) =>
{
    var run = registry.Get(id);
    return run is null ? Results.NotFound() : Results.Ok(ToSummary(run));
});

app.MapPost("/runs/{id:guid}/cancel", (Guid id, FlowRunner runner) =>
{
    var ok = runner.Cancel(id);
    return ok ? Results.Ok() : Results.NotFound();
});

app.Run();

static RunSummary ToSummary(Run run) => new(
    run.Id,
    run.Project,
    run.Flow,
    run.Prompt,
    run.Status.ToString(),
    run.StartedAt,
    run.EndedAt,
    run.SessionId,
    run.SessionDir,
    run.ExitCode,
    run.FailureReason);
