#:project ../../src/RemoteAgents/RemoteAgents.csproj
// flows/full-review.cs
//
// Thin shim. The flow body lives in src/RemoteAgents/Flows/ReviewFlow.cs;
// this file just picks the validator + reviewer wording and dispatches.
//
//   Claude works → orchestrator-validator (Roslyn parse) fix loop →
//   Codex reviews diff → optional revision pass → commit (push opt-in).
//
// Usage:
//   dotnet run flows/full-review.cs <project> "<prompt>" [--push]

using RemoteAgents.Flows;
using RemoteAgents.Validation.Orchestrator;

const string FLOW_NAME = "full-review";

await using var ctx = await FlowBootstrap.StartAsync(args, FLOW_NAME);
if (ctx is null) return;

var flow = new ReviewFlow(
    name:            FLOW_NAME,
    summary:         "Claude works → project checks → Codex review → commit (push opt-in).",
    validator:       new OrchestratorValidator(),
    projectKind:     "changes",
    validationLabel: "all project checks passed");

var result = await new FlowRunner().RunAsync(
    flow, ctx, new FlowArgs(ctx.ProjectName, ctx.UserPrompt, [], ctx.ShouldPush));
Environment.ExitCode = FlowRunner.MapToExitCode(result.Reason);
