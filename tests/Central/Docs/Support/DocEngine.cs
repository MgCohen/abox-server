using System.Diagnostics;

namespace ABox.Tests.Central.Docs.Support;

// Shells out to the standalone doc-engine CLI (tools/doc-engine). ADR 0015: a test type MAY run the doc-engine,
// but the Harness never depends on it — so we invoke it as a process (like Live drives the real claude CLI),
// never as a reference. Built once per run, then invoked per command with --no-build.
public static class DocEngine
{
    public static readonly string ProjectDir = Path.Combine(RepoTree.Root, "tools", "doc-engine");

    // Build and run the engine in the SAME configuration as this test assembly, derived from its own output dir
    // (artifacts/bin/<Project>/<config>) — independent of the spawned process's WorkingDirectory. A pinned -c
    // would build/run a different config than the suite (an extra build + a stale binary risk).
    private static readonly string Config = new DirectoryInfo(
        AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Name;

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    private static readonly Lazy<bool> Built = new(BuildOnce);

    public sealed record Result(int Exit, string Output);

    public static Result Run(params string[] args)
    {
        _ = Built.Value;
        return Exec(new[] { "run", "--project", ProjectDir, "--no-build", "-c", Config, "--" }.Concat(args).ToArray());
    }

    private static bool BuildOnce()
    {
        var r = Exec(new[] { "build", ProjectDir, "-c", Config });
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
        if (!p.WaitForExit((int)Timeout.TotalMilliseconds))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* already gone — nothing to reap */ }
            throw new TimeoutException(
                $"doc-engine did not exit within {Timeout.TotalMinutes:n0}m: dotnet {string.Join(' ', args)}. " +
                "A hung engine would otherwise deadlock the test run.");
        }
        return new Result(p.ExitCode, (stdout.Result + stderr.Result).Trim());
    }
}
