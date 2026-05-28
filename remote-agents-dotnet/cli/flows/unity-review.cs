#:project ../../src/RemoteAgents/RemoteAgents.csproj
// flows/unity-review.cs
//
// Same pipeline as full-review.cs but validates with UnityBatchValidator
// (Unity batch-mode compile) instead of the Roslyn parse. Use against any
// Unity project in projects.json.
//
// Unity batch-mode regenerates TMP_SDF / .meta / Library on every run, so
// the pipeline snapshots Claude-touched files BEFORE validating and
// restores everything else AFTER — see IsolateClaudeChanges.
//
// Usage:
//   dotnet run flows/unity-review.cs <project> "<prompt>" [--push]

using RemoteAgents.Flows;
using RemoteAgents.Validation.Unity;

const string FLOW_NAME = "unity-review";

await using var ctx = await FlowBootstrap.StartAsync(args, FLOW_NAME);
if (ctx is null) return;
if (!await ctx.EnsureCleanTreeAsync()) return;

try
{
    var result = await ReviewPipeline.RunAsync(
        ctx.Session, ctx.Sink, ctx.ProjectDir, ctx.UserPrompt, ctx.ShouldPush,
        validator: new UnityBatchValidator(),
        opts: new ReviewPipelineOptions(
            FlowName: FLOW_NAME,
            IsolateClaudeChanges: true,
            ValidationProgressNote: " (Unity batch-mode, this can take minutes)",
            FixPromptValidationDescriptor: "Unity batch-mode compile",
            ValidationLabel: "Unity batch-mode compile passed",
            ReviewProjectKind: "a Unity C# change"));
    Environment.ExitCode = result.ExitCode;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[{FLOW_NAME}] FAILED: {ex}");
    ctx.Session.End("failed", failureReason: ex.Message);
    Environment.ExitCode = 1;
}
