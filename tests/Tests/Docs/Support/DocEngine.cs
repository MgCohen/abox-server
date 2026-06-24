using System.Diagnostics;

namespace ABox.Tests.Docs.Support;

// Shells out to the standalone doc-engine CLI (tools/doc-engine). ADR 0013: a test type MAY run the doc-engine,
// but the Harness never depends on it — so we invoke it as a process (like Live drives the real claude CLI),
// never as a reference. Built once per run, then invoked per command with --no-build.
public static class DocEngine
{
    public static readonly string ProjectDir = Path.Combine(RepoTree.Root, "tools", "doc-engine");
    public static readonly string OutDir = Path.Combine(ProjectDir, "out");

    private static readonly Lazy<bool> Built = new(BuildOnce);

    public sealed record Result(int Exit, string Output);

    public static Result Run(params string[] args)
    {
        _ = Built.Value;
        return Exec(new[] { "run", "--project", ProjectDir, "--no-build", "-c", "Debug", "--" }.Concat(args).ToArray());
    }

    private static bool BuildOnce()
    {
        var r = Exec(new[] { "build", ProjectDir, "-c", "Debug" });
        if (r.Exit != 0)
            throw new InvalidOperationException($"Could not build the doc-engine at {ProjectDir}:\n{r.Output}");
        return true;
    }

    private static Result Exec(string[] args)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = ProjectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("could not start dotnet");
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return new Result(p.ExitCode, output.Trim());
    }
}
