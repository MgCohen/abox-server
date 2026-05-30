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

var flow = new ReviewFlow(new ReviewFlowOptions(
    Name:              FLOW_NAME,
    Summary:           "Claude works → Unity batch-mode compile → Codex review → commit (push opt-in).",
    Validator:         new UnityFullValidator(),
    ProjectKind:       "a Unity C# change",
    ValidationLabel:   "Unity batch-mode compile passed",
    IsolateValidation: true,
    ProgressNote:      " (Unity batch-mode, this can take minutes)",
    FixDescriptor:     "Unity batch-mode compile"));

var result = await new FlowRunner().RunAsync(
    flow, ctx, new FlowArgs(ctx.ProjectName, ctx.UserPrompt, [], ctx.ShouldPush));
Environment.ExitCode = FlowRunner.MapToExitCode(result.Reason);
