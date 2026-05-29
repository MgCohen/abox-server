#:project ../../src/RemoteAgents/RemoteAgents.csproj
#:project ../../src/RemoteAgents.Hosting/RemoteAgents.Hosting.csproj
// flows/claude-only.cs
//
// Thin shim — resolves the registered ClaudeOnlyFlow from the
// FlowRegistry and dispatches it through FlowRunner. The flow body
// lives in src/RemoteAgents/Flows/ClaudeOnlyFlow.cs (Phase 4 pilot).
//
// Usage:
//   dotnet run flows/claude-only.cs <project> "<prompt>"

using Microsoft.Extensions.DependencyInjection;
using RemoteAgents.Flows;
using RemoteAgents.Hosting;

const string FLOW_NAME = "claude-only";

var services = new ServiceCollection();
services.AddRemoteAgents(o =>
{
    o.UseClaude();
    o.AddFlow<ClaudeOnlyFlow>();
});
var sp = services.BuildServiceProvider();

await using var ctx = await FlowBootstrap.StartAsync(args, FLOW_NAME);
if (ctx is null) return;

var flow = sp.GetRequiredService<FlowRegistry>().Get(FLOW_NAME)!;
var runner = sp.GetRequiredService<FlowRunner>();
var result = await runner.RunAsync(
    flow, ctx,
    new FlowArgs(ctx.ProjectName, ctx.UserPrompt, [], ctx.ShouldPush));
Environment.ExitCode = FlowRunner.MapToExitCode(result.Reason);
