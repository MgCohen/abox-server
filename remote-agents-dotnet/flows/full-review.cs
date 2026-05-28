#:project ../RemoteAgents/RemoteAgents.csproj
// flows/full-review.cs
//
// Claude works → orchestrator-validator (Roslyn parse) fix loop → Codex
// reviews diff → optional revision pass → commit (push opt-in).
//
// Usage:
//   dotnet run flows/full-review.cs <project> "<prompt>" [--push]

using RemoteAgents.Flows;
using RemoteAgents.Validation.Orchestrator;

const string FLOW_NAME = "full-review";

await using var ctx = await FlowBootstrap.StartAsync(args, FLOW_NAME);
if (ctx is null) return;
if (!await ctx.EnsureCleanTreeAsync()) return;

try
{
    var result = await ReviewPipeline.RunAsync(
        ctx.Session, ctx.Sink, ctx.ProjectDir, ctx.UserPrompt, ctx.ShouldPush,
        validator: new OrchestratorValidator(),
        opts: new ReviewPipelineOptions(FlowName: FLOW_NAME));
    Environment.ExitCode = result.ExitCode;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[{FLOW_NAME}] FAILED: {ex}");
    ctx.Session.End("failed", failureReason: ex.Message);
    Environment.ExitCode = 1;
}
