#:project ../../src/RemoteAgents/RemoteAgents.csproj
#:project ../../src/RemoteAgents.Hosting/RemoteAgents.Hosting.csproj
// Step-12 acceptance: run each of the three named agent presets against
// a trivial prompt and confirm they wire up.

using RemoteAgents.Agents;
using RemoteAgents.Events;
using RemoteAgents.Hosting;
using RemoteAgents.Primitives;

const string WORK_DIR = @"C:\Unity\dotnet-pty-smoke\stage2-work";
Directory.CreateDirectory(WORK_DIR);
await SubscriptionGuard.CheckAsync();

var sink = new ConsoleSink();

Console.WriteLine("=== Planner (Claude opus) ===");
var planner = AgentPresets.Planner.Build(sink);
var p = await planner.RunAsync(new AgentRunRequest(
    "Plan, in 2 numbered steps, how to rename a method in a single C# file.",
    null, WORK_DIR));
Console.WriteLine($"\n[planner] exit={p.ExitCode} session={p.SessionId}\n");

Console.WriteLine("=== Documenter (Claude haiku) ===");
var documenter = AgentPresets.Documenter.Build(sink);
var d = await documenter.RunAsync(new AgentRunRequest(
    "In one sentence, describe what a thread pool is.",
    null, WORK_DIR));
Console.WriteLine($"\n[documenter] exit={d.ExitCode} session={d.SessionId}\n");

Console.WriteLine("=== Researcher (Codex gpt-5.5) ===");
var researcher = AgentPresets.Researcher.Build(sink);
var r = await researcher.RunAsync(new AgentRunRequest(
    "What HTTP status code means 'not found'?",
    null, WORK_DIR));
Console.WriteLine($"\n[researcher] exit={r.ExitCode} session={r.SessionId}");
Console.WriteLine($"  text: {r.Text.Trim()}\n");

var allOk = p.ExitCode == 0 && d.ExitCode == 0 && r.ExitCode == 0;
Console.WriteLine(allOk ? "[smoke] all three agents ran clean." : "[smoke] FAILED");
Environment.ExitCode = allOk ? 0 : 1;
