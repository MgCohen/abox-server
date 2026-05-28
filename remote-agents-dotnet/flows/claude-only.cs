#:project ../RemoteAgents/RemoteAgents.csproj
// flows/claude-only.cs
//
// Baseline flow. Hand it a project name and a prompt; it spins up Claude
// inside a PTY against that project's directory, captures whatever
// changed, and writes a session transcript. No validation, no review,
// no git.
//
// Usage:
//   dotnet run flows/claude-only.cs <project> "<prompt>"
//
// Parity reference: remote-agents/orchestrator/flows/claude-only.mjs

using RemoteAgents.Agents;
using RemoteAgents.Events;
using RemoteAgents.Primitives;
using RemoteAgents.Sessions;

const string FLOW_NAME = "claude-only";

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

try
{
    var before = FsDiff.Snapshot(projectDir);

    var claude = new ClaudeAgent { Name = "claude", Sink = sink };
    var result = await claude.RunAsync(new AgentRunRequest(
        Prompt: userPrompt,
        SessionId: null,
        ProjectDir: projectDir));

    // Forensic dumps — useful while PTY timings are still being tuned.
    await File.WriteAllTextAsync(Path.Combine(session.Dir, "claude-raw.txt"), result.RawOutput);
    await File.WriteAllTextAsync(Path.Combine(session.Dir, "claude-text.txt"), result.Text);

    var after = FsDiff.Snapshot(projectDir);
    var diff = FsDiff.Diff(before, after);

    Console.WriteLine();
    Console.WriteLine($"Claude session: {result.SessionId}");
    Console.WriteLine($"Files changed:  {diff.Changed.Count}");
    Console.WriteLine($"Files added:    {diff.Added.Count}");
    Console.WriteLine($"Files removed:  {diff.Removed.Count}");
    foreach (var f in diff.All) Console.WriteLine($"  - {f}");

    session.End("done");
    Console.WriteLine();
    Console.WriteLine($"Transcript: {session.Dir}");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[{FLOW_NAME}] FAILED: {ex}");
    session.End("failed", failureReason: ex.Message);
    Environment.ExitCode = 1;
}
