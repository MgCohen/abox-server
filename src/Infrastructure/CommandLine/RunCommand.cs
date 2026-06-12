using System.Diagnostics;

namespace ABox.Infrastructure.CommandLine;

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
        var psi = Shell.Command(command);
        psi.WorkingDirectory = options.Cwd ?? Environment.CurrentDirectory;
        psi.RedirectStandardInput = options.Input is not null;
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
