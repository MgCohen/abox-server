#:project ../src/RemoteAgents/RemoteAgents.csproj
// agents-dotnet — thin CLI runner. `agents-dotnet run <flow> <args...>`
// executes cli/flows/<flow>.cs with the remaining args forwarded as
// command-line args. Matches the JS bin/agents.js ergonomics.
//
// Usage:
//   dotnet run cli/agents-dotnet.cs run <flow> <args...>
//   dotnet run cli/agents-dotnet.cs list
//   dotnet run cli/agents-dotnet.cs projects

using System.Diagnostics;
using RemoteAgents.Primitives;

// Locate the orchestrator dir (RemoteAgents.slnx lives at its root).
var orchestratorRoot = OrchestratorPaths.FindOrThrow();
var flowsDir = OrchestratorPaths.FlowsDir()!;

var subcommand = args.Length > 0 ? args[0] : null;
var rest = args.Skip(1).ToArray();

switch (subcommand)
{
    case "run":
        await RunFlow(rest);
        break;
    case "list":
        foreach (var f in ListFlows()) Console.WriteLine(f);
        if (ListFlows().Count == 0) Console.WriteLine("(no flows yet)");
        break;
    case "projects":
        foreach (var p in ProjectRegistry.List()) Console.WriteLine(p);
        break;
    case null:
    case "-h":
    case "--help":
    case "help":
        Usage();
        break;
    default:
        Console.Error.WriteLine($"agents-dotnet: unknown subcommand \"{subcommand}\"\n");
        Usage();
        Environment.ExitCode = 2;
        break;
}

void Usage()
{
    var flows = ListFlows();
    var projs = ProjectRegistry.List();
    Console.WriteLine("agents-dotnet — local subscription-billed agent orchestrator (.NET)");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  agents-dotnet run <flow> <args...>     Run a flow with args");
    Console.WriteLine("  agents-dotnet list                     List available flows");
    Console.WriteLine("  agents-dotnet projects                 List configured projects");
    Console.WriteLine();
    Console.WriteLine($"Flows:    {(flows.Count > 0 ? string.Join(", ", flows) : "(none yet — add files to cli/flows/)")}");
    Console.WriteLine($"Projects: {(projs.Count > 0 ? string.Join(", ", projs) : "(none yet — see projects.json)")}");
}

List<string> ListFlows()
{
    if (!Directory.Exists(flowsDir)) return new();
    return Directory.EnumerateFiles(flowsDir, "*.cs", SearchOption.TopDirectoryOnly)
        .Select(f => Path.GetFileNameWithoutExtension(f))
        .Where(f => !f.StartsWith("smoke-"))
        .OrderBy(f => f)
        .ToList();
}

async Task RunFlow(string[] flowArgs)
{
    if (flowArgs.Length == 0)
    {
        Console.Error.WriteLine($"agents-dotnet run: missing flow name. Available: {string.Join(", ", ListFlows())}");
        Environment.ExitCode = 2;
        return;
    }

    var name = flowArgs[0];
    var passthrough = flowArgs.Skip(1).ToArray();

    var flowPath = Path.Combine(flowsDir, name + ".cs");
    if (!File.Exists(flowPath))
    {
        Console.Error.WriteLine($"agents-dotnet run: flow \"{name}\" not found at {flowPath}");
        Console.Error.WriteLine($"Available:  {string.Join(", ", ListFlows())}");
        Environment.ExitCode = 2;
        return;
    }

    // dotnet run flows/<name>.cs -- <passthrough...>
    var psi = new ProcessStartInfo
    {
        FileName = "dotnet",
        WorkingDirectory = orchestratorRoot,
        UseShellExecute = false,
    };
    psi.ArgumentList.Add("run");
    psi.ArgumentList.Add(flowPath);
    if (passthrough.Length > 0)
    {
        psi.ArgumentList.Add("--");
        foreach (var a in passthrough) psi.ArgumentList.Add(a);
    }

    using var proc = Process.Start(psi)!;
    await proc.WaitForExitAsync();
    Environment.ExitCode = proc.ExitCode;
}
