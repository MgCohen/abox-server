#:project ../RemoteAgents/RemoteAgents.csproj
// Step-15 pre-check: confirm UnityBatchValidator passes against a clean
// Unity project before we burn Claude+Codex turns on the full pipeline.

using RemoteAgents.Primitives;
using RemoteAgents.Validation.Unity;

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: dotnet run smoke-unity-validate.cs <project>");
    Environment.ExitCode = 2;
    return;
}

var projectDir = ProjectRegistry.Resolve(args[0]);
Console.WriteLine($"[smoke] projectDir = {projectDir}");

var v = new UnityBatchValidator();
Console.WriteLine($"[smoke] unityExePath = {v.UnityExePath}");
Console.WriteLine("[smoke] running validator (may take several minutes)...");

var sw = System.Diagnostics.Stopwatch.StartNew();
var result = await v.ValidateAsync(projectDir);
Console.WriteLine($"[smoke] {(result.Ok ? "OK" : "FAIL")} in {sw.Elapsed.TotalSeconds:F1}s");
Console.WriteLine($"[smoke] summary: {result.Summary}");
if (!result.Ok)
{
    Console.WriteLine("[smoke] errors (tail):");
    Console.WriteLine(result.Errors);
}
Environment.ExitCode = result.Ok ? 0 : 1;
