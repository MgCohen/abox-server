#:project ../../src/RemoteAgents/RemoteAgents.csproj
// flows/claude-only.cs
//
// Baseline flow. Hand it a project name and a prompt; it spins up Claude
// inside a PTY against that project's directory, captures whatever
// changed, and writes a session transcript. No validation, no review,
// no git.
//
// Usage:
//   dotnet run flows/claude-only.cs <project> "<prompt>"

using RemoteAgents.Agents;
using RemoteAgents.Flows;
using RemoteAgents.Primitives;
using RemoteAgents.Sessions;

const string FLOW_NAME = "claude-only";

await using var ctx = await FlowBootstrap.StartAsync(args, FLOW_NAME);
if (ctx is null) return;

try
{
    var before = FsDiff.Snapshot(ctx.ProjectDir);

    var claude = new ClaudeAgent { Name = "claude", Sink = ctx.Sink };
    var result = await claude.RunAsync(new AgentRunRequest(
        Prompt: ctx.UserPrompt,
        SessionId: null,
        ProjectDir: ctx.ProjectDir));

    // Forensic dumps — useful while PTY timings are still being tuned.
    await File.WriteAllTextAsync(Path.Combine(ctx.Session.Dir, "claude-raw.txt"), result.RawOutput);
    await File.WriteAllTextAsync(Path.Combine(ctx.Session.Dir, "claude-text.txt"), result.Text);

    var after = FsDiff.Snapshot(ctx.ProjectDir);
    var diff = FsDiff.Diff(before, after);

    Console.WriteLine();
    Console.WriteLine($"Claude session: {result.SessionId}");
    Console.WriteLine($"Files changed:  {diff.Changed.Count}");
    Console.WriteLine($"Files added:    {diff.Added.Count}");
    Console.WriteLine($"Files removed:  {diff.Removed.Count}");
    foreach (var f in diff.All) Console.WriteLine($"  - {f}");

    ctx.Session.End(SessionResult.Shipped);
    Console.WriteLine();
    Console.WriteLine($"Transcript: {ctx.Session.Dir}");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[{FLOW_NAME}] FAILED: {ex}");
    ctx.Session.End(SessionResult.Failed, failureReason: ex.Message);
    Environment.ExitCode = 1;
}
