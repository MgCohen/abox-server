#:project ../../src/RemoteAgents/RemoteAgents.csproj
// flows/claude-validate.cs
//
// Phase 2 flow: Claude does the work → project validator runs → if it
// fails, resume the Claude session and feed the failure back → retry up
// to N times. No Codex review yet, no auto-commit. The hand-written
// while-loop is the entire iteration policy; tune the cap by editing
// this file.
//
// Usage:
//   dotnet run flows/claude-validate.cs <project> "<prompt>"

using RemoteAgents.Agents;
using RemoteAgents.Events;
using RemoteAgents.Flows;
using RemoteAgents.Primitives;
using RemoteAgents.Sessions;
using RemoteAgents.Validation;
using RemoteAgents.Validation.Orchestrator;

const string FLOW_NAME = "claude-validate";
const int MAX_FIX_ATTEMPTS = 3;

await using var ctx = await FlowBootstrap.StartAsync(args, FLOW_NAME);
if (ctx is null) return;

IValidator validator = new OrchestratorValidator();

try
{
    var before = FsDiff.Snapshot(ctx.ProjectDir);

    var claude = new ClaudeAgent { Name = "claude", Sink = ctx.Sink };

    // ── 1. initial Claude run ─────────────────────────────────────────
    var result = await claude.RunAsync(new AgentRunRequest(ctx.UserPrompt, null, ctx.ProjectDir));
    await ctx.Sink.PhaseOkAsync("claude", $"turn 1 done (session={result.SessionId})");

    // ── 2. validate + fix loop ────────────────────────────────────────
    bool validationOk = false;
    int attempt = 0;
    ValidationResult v = new(false, "", "");

    while (attempt < MAX_FIX_ATTEMPTS)
    {
        attempt++;
        await ctx.Sink.PhaseStartAsync("validate", $"attempt {attempt}...");
        v = await validator.ValidateAsync(ctx.ProjectDir);
        if (v.Ok) { await ctx.Sink.PhaseOkAsync("validate", $"PASSED — {v.Summary}"); validationOk = true; break; }
        await ctx.Sink.PhaseFailAsync("validate", $"FAILED — {v.Summary}");
        if (attempt >= MAX_FIX_ATTEMPTS) break;

        var fixPrompt =
            $"The previous changes failed validation. Address these issues:\n\n{v.Errors}\n\n" +
            "Make whatever edits are necessary, then I'll re-run validation.";

        result = await claude.RunAsync(new AgentRunRequest(fixPrompt, result.SessionId, ctx.ProjectDir));
        await ctx.Sink.PhaseOkAsync("claude", $"fix turn {attempt + 1} done");
    }

    // ── 3. summary ────────────────────────────────────────────────────
    var after = FsDiff.Snapshot(ctx.ProjectDir);
    var diff = FsDiff.Diff(before, after);

    await ctx.Session.WriteArtifactAsync(SessionArtifact.ClaudeRaw, result.RawOutput);
    await ctx.Session.WriteArtifactAsync(SessionArtifact.ClaudeText, result.Text);

    Console.WriteLine($"Result:         {(validationOk ? "VALIDATION PASSED" : "VALIDATION FAILED")}");
    Console.WriteLine($"Attempts:       {attempt}");
    Console.WriteLine($"Claude session: {result.SessionId}");
    Console.WriteLine($"Files changed:  {diff.Changed.Count}");
    Console.WriteLine($"Files added:    {diff.Added.Count}");
    Console.WriteLine($"Files removed:  {diff.Removed.Count}");
    foreach (var f in diff.All) Console.WriteLine($"  - {f}");
    Console.WriteLine();
    Console.WriteLine($"Transcript: {ctx.Session.Dir}");

    ctx.Session.End(validationOk ? SessionResult.Ok : SessionResult.ValidationFailed);
    Environment.ExitCode = validationOk ? 0 : 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[{FLOW_NAME}] FAILED: {ex}");
    ctx.Session.End(SessionResult.Failed, failureReason: ex.Message);
    Environment.ExitCode = 1;
}
