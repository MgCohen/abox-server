using RemoteAgents.Host.Hubs;
using RemoteAgents.Host.Runs;
using RemoteAgents.Hosting;
using RemoteAgents.Primitives;
using RemoteAgents.Runs;
using RemoteAgents.Wire;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<RunRegistry>();
builder.Services.AddSingleton<RunStore>();
builder.Services.AddSingleton<FlowRunner>();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

// Composition root for the library: flow dispatch (FlowRegistry + FlowRunner)
// and any cross-cutting sinks. Agents are constructed by the flows that need
// them, not resolved here — see RemoteAgentsOptions.
builder.Services.AddRemoteAgents(_ => { });

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

// Seed history from disk + mark active-on-shutdown runs as Interrupted.
using (var scope = app.Services.CreateScope())
{
    var store = scope.ServiceProvider.GetRequiredService<RunStore>();
    var registry = scope.ServiceProvider.GetRequiredService<RunRegistry>();
    var persisted = await store.LoadAsync();
    registry.SeedHistory(persisted);
    app.Logger.LogInformation("Loaded {Count} persisted runs from {Path}", persisted.Length, store.FilePath);
}

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
    return Results.Accepted($"/runs/{run.Id}", RunRegistry.ToRecord(run));
});

app.MapGet("/runs", (RunRegistry registry) => Results.Ok(registry.List()));

app.MapGet("/runs/{id:guid}", (Guid id, RunRegistry registry) =>
{
    var live = registry.Get(id);
    if (live is not null) return Results.Ok(RunRegistry.ToRecord(live));
    var hist = registry.HistorySnapshot().FirstOrDefault(p => p.Id == id);
    return hist is null ? Results.NotFound() : Results.Ok(hist);
});

app.MapPost("/runs/{id:guid}/cancel", (Guid id, FlowRunner runner) =>
{
    var ok = runner.Cancel(id);
    return ok ? Results.Ok() : Results.NotFound();
});

// What the agent actually said this turn (the library's distilled
// claude-text.txt / codex output). Useful as the "final answer" panel
// in the UI — the raw PTY stream is too noisy to read as is.
app.MapGet("/runs/{id:guid}/output", async (Guid id, RunRegistry registry) =>
{
    var run = registry.Get(id);
    var sessionDir = run?.SessionDir
        ?? registry.HistorySnapshot().FirstOrDefault(p => p.Id == id)?.SessionDir;
    if (sessionDir is null) return Results.NotFound();

    var candidates = new[]
    {
        RemoteAgents.Sessions.Session.GetArtifactPath(sessionDir, RemoteAgents.Sessions.SessionArtifact.ClaudeText),
        RemoteAgents.Sessions.Session.GetArtifactPath(sessionDir, RemoteAgents.Sessions.SessionArtifact.CodexReview),
    };
    foreach (var path in candidates)
    {
        if (File.Exists(path))
        {
            var text = await File.ReadAllTextAsync(path);
            return Results.Text(text, "text/plain");
        }
    }
    return Results.NoContent();
});

// Forward-compat for the AgentQuestion answer-back loop. The library's v2
// contract for routing responses into a paused agent is not yet defined
// (see PLANS/interaction-modes.md Q10), so v1 just records the choice on
// the run. The wire shape is locked now so the UI can be built against
// it without changing later.
app.MapPost("/runs/{id:guid}/respond", (Guid id, RespondRequest req, RunRegistry registry) =>
{
    var run = registry.Get(id);
    if (run is null) return Results.NotFound();
    if (string.IsNullOrWhiteSpace(req.Choice)) return Results.BadRequest(new { error = "choice required" });

    run.PendingQuestionCorrelationId = req.CorrelationId;
    run.PendingResponse = req.Choice;
    run.RespondedAt = DateTimeOffset.UtcNow;
    return Results.Accepted();
});

app.Run();

// Run → RunRecord is the single projection now (RunRegistry.ToRecord);
// persisted history is already RunRecord. No per-shape adapters remain.
