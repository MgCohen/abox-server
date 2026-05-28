#:project ../RemoteAgents/RemoteAgents.csproj
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
using RemoteAgents.Primitives;
using RemoteAgents.Sessions;
using RemoteAgents.Validation;
using RemoteAgents.Validation.Orchestrator;

const string FLOW_NAME = "claude-validate";
const int MAX_FIX_ATTEMPTS = 3;

if (args.Length < 2)
{
    Console.Error.WriteLine($"Usage: dotnet run flows/{FLOW_NAME}.cs <project> \"<prompt>\"");
    Environment.ExitCode = 2;
    return;
}

var projectName = args[0];
var userPrompt = string.Join(' ', args[1..]).Trim();

await SubscriptionGuard.CheckAsync();

var projectDir = ProjectRegistry.Resolve(projectName);
var session = Session.Start(new StartSessionRequest(
    ProjectDir: projectDir,
    ProjectName: projectName,
    UserPrompt: userPrompt,
    FlowName: FLOW_NAME));

Console.WriteLine($"[{session.Id}]");
Console.WriteLine($"  flow:    {FLOW_NAME}");
Console.WriteLine($"  project: {projectName} ({projectDir})");
Console.WriteLine($"  prompt:  {userPrompt}");
Console.WriteLine();

var sink = new CompositeSink(
    new ConsoleSink(),
    new JsonlSink(session.TranscriptFile),
    new ProviderJsonlIngestSink(session.Dir, projectDir));

IValidator validator = new OrchestratorValidator();

try
{
    var before = FsDiff.Snapshot(projectDir);

    var claude = new ClaudeAgent { Name = "claude", Sink = sink };

    // ── 1. initial Claude run ─────────────────────────────────────────
    var result = await claude.RunAsync(new AgentRunRequest(userPrompt, null, projectDir));
    Console.WriteLine($"[claude] turn 1 done (session={result.SessionId})\n");

    // ── 2. validate + fix loop ────────────────────────────────────────
    bool validationOk = false;
    int attempt = 0;
    ValidationResult v = new(false, "", "");

    while (attempt < MAX_FIX_ATTEMPTS)
    {
        attempt++;
        Console.WriteLine($"[validate] attempt {attempt}...");
        v = await validator.ValidateAsync(projectDir);
        Console.WriteLine($"[validate] {(v.Ok ? "PASSED" : "FAILED")} — {v.Summary}");

        if (v.Ok) { validationOk = true; break; }
        if (attempt >= MAX_FIX_ATTEMPTS) break;

        var fixPrompt =
            $"The previous changes failed validation. Address these issues:\n\n{v.Errors}\n\n" +
            "Make whatever edits are necessary, then I'll re-run validation.";

        result = await claude.RunAsync(new AgentRunRequest(fixPrompt, result.SessionId, projectDir));
        Console.WriteLine($"[claude] fix turn {attempt + 1} done\n");
    }

    // ── 3. summary ────────────────────────────────────────────────────
    var after = FsDiff.Snapshot(projectDir);
    var diff = FsDiff.Diff(before, after);

    await File.WriteAllTextAsync(Path.Combine(session.Dir, "claude-raw.txt"), result.RawOutput);
    await File.WriteAllTextAsync(Path.Combine(session.Dir, "claude-text.txt"), result.Text);

    Console.WriteLine("──────────────────────────────────────────");
    Console.WriteLine($"Result:         {(validationOk ? "VALIDATION PASSED" : "VALIDATION FAILED")}");
    Console.WriteLine($"Attempts:       {attempt}");
    Console.WriteLine($"Claude session: {result.SessionId}");
    Console.WriteLine($"Files changed:  {diff.Changed.Count}");
    Console.WriteLine($"Files added:    {diff.Added.Count}");
    Console.WriteLine($"Files removed:  {diff.Removed.Count}");
    foreach (var f in diff.All) Console.WriteLine($"  - {f}");
    Console.WriteLine();
    Console.WriteLine($"Transcript: {session.Dir}");

    session.End(validationOk ? "validated" : "validation-failed");
    Environment.ExitCode = validationOk ? 0 : 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[{FLOW_NAME}] FAILED: {ex}");
    session.End("failed", failureReason: ex.Message);
    Environment.ExitCode = 1;
}
