using System.Text.Json;
using RemoteAgents.Flows;
using RemoteAgents.Host.Hubs;
using RemoteAgents.Host.Runs;
using RemoteAgents.Hosting;
using RemoteAgents.Primitives;
using RemoteAgents.Runs;
using RemoteAgents.Validation.Orchestrator;
using RemoteAgents.Validation.Unity;
using RemoteAgents.Wire;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<RunRegistry>();
builder.Services.AddSingleton<RunStore>();

// Flow executors — registration order = CanHandle priority. Step 2 lands
// both implementations but only the subprocess one wins because the
// FlowCatalog is left empty; step 3 flips the order and populates the
// registry to switch the Host onto the in-process path.
builder.Services.AddSingleton<FlowCatalog>();
builder.Services.AddSingleton<IFlowExecutor, InProcessFlowExecutor>();
builder.Services.AddSingleton<IFlowExecutor>(sp => new SubprocessFlowExecutor(
    OrchestratorPaths.FindOrThrow(),
    sp.GetRequiredService<ILogger<SubprocessFlowExecutor>>()));
builder.Services.AddSingleton<RemoteAgents.Host.Runs.FlowRunner>();

// New runtime FlowRegistry path (Workstream B). Coexists with the legacy
// Run/RunRegistry path during the migration; UI flips to /flows + SSE in
// a follow-up commit, then the legacy path is deleted.
builder.Services.AddSingleton<IHistoryStore>(_ => new FileHistoryStore(FileHistoryStore.DefaultPath()));
builder.Services.AddSingleton<RemoteAgents.Hosting.FlowRegistry>();

builder.Services.AddOpenApi();
builder.Services.AddSignalR();

// Composition root for the library: flow dispatch (FlowCatalog + RemoteAgents.Host.Runs.FlowRunner)
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

// Populate the in-process FlowCatalog — names match cli/flows/*.cs so
// the InProcessFlowExecutor's CanHandle resolves to a registered IFlow
// for the named flows below, falling back to SubprocessFlowExecutor for
// any other flow name a client POSTs.
{
    var flows = app.Services.GetRequiredService<FlowCatalog>();
    flows.Register(new ClaudeOnlyFlow());
    flows.Register(new ReviewFlow(new ReviewFlowOptions(
        Name:            "full-review",
        Summary:         "Claude works → project checks → Codex review → commit (push opt-in).",
        Validator:       new OrchestratorValidator(),
        ProjectKind:     "changes",
        ValidationLabel: "all project checks passed")));
    flows.Register(new ReviewFlow(new ReviewFlowOptions(
        Name:              "unity-review",
        Summary:           "Claude works → Unity batch-mode compile → Codex review → commit (push opt-in).",
        Validator:         new UnityFullValidator(),
        ProjectKind:       "a Unity C# change",
        ValidationLabel:   "Unity batch-mode compile passed",
        IsolateValidation: true,
        ProgressNote:      " (Unity batch-mode, this can take minutes)",
        FixDescriptor:     "Unity batch-mode compile")));
}

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

// ---- Catalog (available flow definitions) -----------------------------
// Renamed from GET /flows so /flows can host the runtime registry surface
// per the plan (Workstream B). The catalog is "what can I run", the
// runtime registry is "what is running / what just ran".

app.MapGet("/catalog", (RemoteAgents.Host.Runs.FlowRunner runner) =>
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

app.MapPost("/runs", (StartRunRequest req, RemoteAgents.Host.Runs.FlowRunner runner) =>
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

app.MapPost("/runs/{id:guid}/cancel", (Guid id, RemoteAgents.Host.Runs.FlowRunner runner) =>
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

// ---- Runtime Flows (Workstream B — new path) -------------------------
// Snapshot + SSE surface. POST /flows starts a flow via LegacyFlowAdapter
// (until step 7 ports each flow native); GET /flows/{id}/events streams
// FlowSnapshot at each completion boundary (per D3). The legacy /runs
// path still operates alongside this until the UI is flipped over.

app.MapPost("/flows", (StartRunRequest req, RemoteAgents.Hosting.FlowRegistry registry, FlowCatalog catalog) =>
{
    if (string.IsNullOrWhiteSpace(req.Project)) return Results.BadRequest(new { error = "project required" });
    if (string.IsNullOrWhiteSpace(req.Flow))    return Results.BadRequest(new { error = "flow required" });
    if (string.IsNullOrWhiteSpace(req.Prompt))  return Results.BadRequest(new { error = "prompt required" });

    var iflow = catalog.Get(req.Flow);
    if (iflow is null) return Results.BadRequest(new { error = $"unknown flow '{req.Flow}'" });

    string projectDir;
    try { projectDir = ProjectRegistry.Resolve(req.Project); }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }

    var adapter = new LegacyFlowAdapter(iflow, req.Project, projectDir, req.Prompt, req.Args ?? []);
    var id = registry.Start(adapter);
    var snap = registry.Get(id)!;
    return Results.Accepted($"/flows/{id}", snap);
});

app.MapGet("/flows", (RemoteAgents.Hosting.FlowRegistry registry) =>
    Results.Ok(registry.All()));

app.MapGet("/flows/{id:guid}", (Guid id, HttpContext ctx, RemoteAgents.Hosting.FlowRegistry registry) =>
{
    var snap = registry.Get(id);
    if (snap is null) return Results.NotFound();

    var etag = $"\"{snap.Version}\"";
    if (ctx.Request.Headers.IfNoneMatch == etag) return Results.StatusCode(304);
    ctx.Response.Headers.ETag = etag;
    return Results.Ok(snap);
});

app.MapGet("/flows/{id:guid}/events", async (Guid id, HttpContext ctx, RemoteAgents.Hosting.FlowRegistry registry) =>
{
    var flow = registry.Live(id);
    if (flow is null)
    {
        // Finished or unknown — return one snapshot if we have it, then close.
        var finished = registry.Get(id);
        if (finished is null) { ctx.Response.StatusCode = 404; return; }
        ctx.Response.Headers.ContentType = "text/event-stream";
        await ctx.Response.WriteAsync($"data: {SerializeSnapshot(finished)}\n\n");
        return;
    }

    ctx.Response.Headers.ContentType = "text/event-stream";
    ctx.Response.Headers.CacheControl = "no-cache";
    await ctx.Response.WriteAsync($"data: {SerializeSnapshot(flow.Snapshot())}\n\n");
    await ctx.Response.Body.FlushAsync();
    try
    {
        await foreach (var s in flow.Changes(ctx.RequestAborted))
        {
            await ctx.Response.WriteAsync($"data: {SerializeSnapshot(s)}\n\n");
            await ctx.Response.Body.FlushAsync();
        }
    }
    catch (OperationCanceledException) { /* client disconnected */ }
});

app.MapPost("/flows/{id:guid}/cancel", (Guid id, RemoteAgents.Hosting.FlowRegistry registry) =>
    registry.Cancel(id) ? Results.Ok() : Results.NotFound());

app.MapPost("/flows/{id:guid}/answer", (Guid id, RespondRequest req, RemoteAgents.Hosting.FlowRegistry registry) =>
{
    if (string.IsNullOrWhiteSpace(req.Choice)) return Results.BadRequest(new { error = "choice required" });
    return registry.Answer(id, req.Choice) ? Results.Ok() : Results.NotFound();
});

app.Run();

static string SerializeSnapshot(FlowSnapshot s) =>
    JsonSerializer.Serialize(s, FlowJsonContext.Default.FlowSnapshot);

// Run → RunRecord is the single projection now (RunRegistry.ToRecord);
// persisted history is already RunRecord. No per-shape adapters remain.
