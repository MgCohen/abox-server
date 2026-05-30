#:project ../../src/RemoteAgents/RemoteAgents.csproj
// flows/claude-only.cs
//
// Thin shim — the flow body lives in src/RemoteAgents/Flows/ClaudeOnlyFlow.cs.
// A single-flow CLI script builds its flow directly and dispatches through
// FlowRunner; the FlowCatalog/DI path is for the Host, which resolves many
// flows by name.
//
// Usage:
//   dotnet run flows/claude-only.cs <project> "<prompt>"

using RemoteAgents.Flows;

const string FLOW_NAME = "claude-only";

await using var ctx = await FlowBootstrap.StartAsync(args, FLOW_NAME);
if (ctx is null) return;

var result = await new FlowRunner().RunAsync(
    new ClaudeOnlyFlow(), ctx, new FlowArgs(ctx.ProjectName, ctx.UserPrompt, [], ctx.ShouldPush));
Environment.ExitCode = FlowRunner.MapToExitCode(result.Reason);
