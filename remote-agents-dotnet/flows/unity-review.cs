#:project ../RemoteAgents/RemoteAgents.csproj
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

using RemoteAgents.Events;
using RemoteAgents.Flows;
using RemoteAgents.Primitives;
using RemoteAgents.Sessions;
using RemoteAgents.Validation;
using RemoteAgents.Validation.Unity;

const string FLOW_NAME = "unity-review";

var argv = args.ToList();
var pushIdx = argv.IndexOf("--push");
var shouldPush = pushIdx >= 0;
if (shouldPush) argv.RemoveAt(pushIdx);

if (argv.Count < 2)
{
    Console.Error.WriteLine($"Usage: dotnet run flows/{FLOW_NAME}.cs <project> \"<prompt>\" [--push]");
    Environment.ExitCode = 2;
    return;
}

var projectName = argv[0];
var userPrompt = string.Join(' ', argv.Skip(1)).Trim();

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
Console.WriteLine($"  push:    {(shouldPush ? "yes" : "no")}");
Console.WriteLine();

if (await GitOps.IsDirtyAsync(projectDir))
{
    Console.Error.WriteLine("[abort] working tree is dirty. Commit or stash first.");
    session.End("aborted-dirty-tree");
    Environment.ExitCode = 2;
    return;
}

using var jsonl = new JsonlSink(session.TranscriptFile);
var sink = new CompositeSink(
    new ConsoleSink(),
    jsonl,
    new ProviderJsonlIngestSink(session.Dir, projectDir));

try
{
    var result = await ReviewPipeline.RunAsync(
        session, sink, projectDir, userPrompt, shouldPush,
        validator: new UnityBatchValidator(),
        opts: new ReviewPipelineOptions(
            FlowName: FLOW_NAME,
            IsolateClaudeChanges: true,
            ValidationLabel: "Unity batch-mode compile passed",
            ReviewProjectKind: "a Unity C# change"));
    Environment.ExitCode = result.ExitCode;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[{FLOW_NAME}] FAILED: {ex}");
    session.End("failed", failureReason: ex.Message);
    Environment.ExitCode = 1;
}
