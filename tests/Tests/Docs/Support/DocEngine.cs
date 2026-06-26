using System.Diagnostics;

namespace ABox.Tests.Docs.Support;

// Shells out to the standalone doc-engine CLI (tools/doc-engine). ADR 0015: a test type MAY run the doc-engine,
// but the Harness never depends on it — so we invoke it as a process (like Live drives the real claude CLI),
// never as a reference. Built once per run, then invoked per command with --no-build.
public static class DocEngine
{
    public static readonly string ProjectDir = Path.Combine(RepoTree.Root, "tools", "doc-engine");

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
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return new Result(p.ExitCode, (stdout.Result + stderr.Result).Trim());
    }
}
