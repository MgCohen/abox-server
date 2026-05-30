using System.Text.Json;
using RemoteAgents.Flows;
using RemoteAgents.Hosting;
using RemoteAgents.Primitives;
using RemoteAgents.Validation.Orchestrator;
using RemoteAgents.Validation.Unity;
using RemoteAgents.Wire;

var builder = WebApplication.CreateBuilder(args);

// Runtime FlowRegistry path (Workstream B). The Host is a thin HTTP/SSE
// surface over the in-process orchestrator — no Process.Start, no
// transcript tailing, no SignalR (per Workstream C).
builder.Services.AddSingleton<IHistoryStore>(_ => new FileHistoryStore(FileHistoryStore.DefaultPath()));
builder.Services.AddSingleton<FlowRegistry>();

builder.Services.AddOpenApi();

// Composition root for the library: FlowCatalog + any cross-cutting sinks.
// Agents are constructed by the flows that need them — see RemoteAgentsOptions.
builder.Services.AddRemoteAgents(_ => { });

// CORS so a browser-served WASM bundle on a different origin can hit us.
// Transport security in v1 is Tailscale-only (see plan non-goals).
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .SetIsOriginAllowed(_ => true)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();
app.UseCors();

// Populate the FlowCatalog — names match cli/flows/*.cs.
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

app.MapGet("/catalog", (FlowCatalog catalog) =>
    Results.Ok(catalog.All()
        .OrderBy(f => f.Name)
        .Select(f => new FlowInfo(f.Name, f.Summary))
        .ToArray()));

// ---- Runtime Flows (snapshot + SSE surface) ---------------------------

app.MapPost("/flows", (StartRunRequest req, FlowRegistry registry, FlowCatalog catalog) =>
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

app.MapGet("/flows", (FlowRegistry registry) =>
    Results.Ok(registry.All()));

app.MapGet("/flows/{id:guid}", (Guid id, HttpContext ctx, FlowRegistry registry) =>
{
    var snap = registry.Get(id);
    if (snap is null) return Results.NotFound();

    var etag = $"\"{snap.Version}\"";
    if (ctx.Request.Headers.IfNoneMatch == etag) return Results.StatusCode(304);
    ctx.Response.Headers.ETag = etag;
    return Results.Ok(snap);
});

app.MapGet("/flows/{id:guid}/events", async (Guid id, HttpContext ctx, FlowRegistry registry) =>
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

app.MapPost("/flows/{id:guid}/cancel", (Guid id, FlowRegistry registry) =>
    registry.Cancel(id) ? Results.Ok() : Results.NotFound());

app.MapPost("/flows/{id:guid}/answer", (Guid id, RespondRequest req, FlowRegistry registry) =>
{
    if (string.IsNullOrWhiteSpace(req.Choice)) return Results.BadRequest(new { error = "choice required" });
    return registry.Answer(id, req.Choice) ? Results.Ok() : Results.NotFound();
});

app.Run();

static string SerializeSnapshot(FlowSnapshot s) =>
    JsonSerializer.Serialize(s, FlowJsonContext.Default.FlowSnapshot);
