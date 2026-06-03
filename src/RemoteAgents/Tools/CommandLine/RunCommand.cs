using System.Diagnostics;

namespace RemoteAgents.Tools.CommandLine;

public static class RunCommand
{
    public static async Task<RunCommandResult> RunAsync(
        string command,
        RunCommandOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new RunCommandOptions();
        var sw = Stopwatch.StartNew();
        var psi = BuildShellPsi(command, options);

        await using var session = SubprocessSession.Start(psi, ct);

        if (options.Input is not null)
        {
            await session.StandardInput.WriteAsync(options.Input);
            session.CompleteStdin();
        }

        var exitCode = await session.WaitForExitAsync(options.TimeoutMs, ct);

        return new RunCommandResult(
            Command: command,
            ExitCode: exitCode,
            Stdout: session.RawStdout,
            Stderr: session.RawStderr,
            TimedOut: session.TimedOut,
            DurationMs: sw.ElapsedMilliseconds);
    }

    private static ProcessStartInfo BuildShellPsi(string command, RunCommandOptions options)
    {
        var psi = new ProcessStartInfo
        {
            WorkingDirectory = options.Cwd ?? Environment.CurrentDirectory,
            RedirectStandardInput = options.Input is not null,
        };
        if (OperatingSystem.IsWindows())
        {
            psi.FileName = Shell.CmdExePath;
            psi.Arguments = $"/c {command}";
        }
        else
        {
            // bash needs the whole command as a single -c argument; ArgumentList lets
            // the runtime escape it instead of re-quoting it ourselves.
            psi.FileName = "/bin/bash";
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
        }
        ApplyEnv(psi, options.Env);
        return psi;
    }

    private static void ApplyEnv(ProcessStartInfo psi, IDictionary<string, string?>? overrides)
    {
        if (overrides is null) return;
        foreach (var (k, v) in overrides)
        {
            if (v is null) psi.Environment.Remove(k);
            else psi.Environment[k] = v;
        }
    }
}
