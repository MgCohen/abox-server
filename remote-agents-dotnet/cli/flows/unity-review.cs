#:project ../../src/RemoteAgents/RemoteAgents.csproj
// flows/unity-review.cs
//
// Thin shim. The flow body lives in src/RemoteAgents/Flows/ReviewFlow.cs;
// this file just picks the validator + reviewer wording and dispatches.
//
//   Claude works → UnityBatchValidator (batch-mode compile) fix loop →
//   Codex reviews diff → optional revision pass → commit (push opt-in).
//
// Unity batch-mode regenerates TMP_SDF / .meta / Library on every run, so
// the validate/fix loop runs inside an IsolationScope (isolateValidation:
// true) that restores the validator's noise at scope exit.
//
// Usage:
//   dotnet run flows/unity-review.cs <project> "<prompt>" [--push]

using RemoteAgents.Flows;
using RemoteAgents.Validation.Unity;

const string FLOW_NAME = "unity-review";

await using var ctx = await FlowBootstrap.StartAsync(args, FLOW_NAME);
if (ctx is null) return;

var flow = new ReviewFlow(
    name:              FLOW_NAME,
    summary:           "Claude works → Unity batch-mode compile → Codex review → commit (push opt-in).",
    validator:         new UnityFullValidator(),
    projectKind:       "a Unity C# change",
    validationLabel:   "Unity batch-mode compile passed",
    isolateValidation: true,
    progressNote:      " (Unity batch-mode, this can take minutes)",
    fixDescriptor:     "Unity batch-mode compile");

var result = await new FlowRunner().RunAsync(
    flow, ctx, new FlowArgs(ctx.ProjectName, ctx.UserPrompt, [], ctx.ShouldPush));
Environment.ExitCode = FlowRunner.MapToExitCode(result.Reason);
