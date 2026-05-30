using System.Diagnostics;

namespace RemoteAgents.Primitives;

public sealed record RunCommandOptions(
    string? Cwd = null,
    IDictionary<string, string?>? Env = null,
    int TimeoutMs = 5 * 60_000,
    string? Input = null);

public sealed record RunCommandResult(
    string Command,
    int ExitCode,
    string Stdout,
    string Stderr,
    bool TimedOut,
    long DurationMs)
{
    // Picks the most useful error text: Stderr when present, otherwise
    // Stdout. Many CLIs we wrap (git, gh, dotnet) leave Stderr empty and
    // print failure messages on Stdout; the inverse is also common.
    public string ErrorText => string.IsNullOrEmpty(Stderr) ? Stdout : Stderr;

    // Throws InvalidOperationException with `<op> failed: <ErrorText>` on
    // non-zero exit, otherwise returns this. Replaces ~15 hand-rolled
    // throw-on-nonzero blocks scattered through GitOps/GhOps.
    public RunCommandResult EnsureOk(string op) =>
        ExitCode == 0 ? this : throw new InvalidOperationException($"{op} failed: {ErrorText}");
}

// Buffered facade over SubprocessSession: build a platform-shell PSI,
// run to completion, accumulate stdout/stderr, return a RunCommandResult.
// The spawn / pump / timeout / kill logic lives in SubprocessSession.
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

    // Always go through the platform shell so callers can write
    // pipes / chains. cmd.exe on Windows, /bin/bash on Linux/macOS.
    // The VM runs Linux — the bash branch is what Track B uses in
    // production; cmd.exe is what local dev hits.
    //
    // Quoting differs: cmd.exe takes the whole rest-of-line as the
    // command after /c; bash needs -c "<command>" as one arg or it
    // treats following tokens as positional ($0, $1, ...). We use
    // ArgumentList on bash so the runtime escapes for us.
    private static ProcessStartInfo BuildShellPsi(string command, RunCommandOptions options)
    {
        var psi = new ProcessStartInfo
        {
            WorkingDirectory = options.Cwd ?? Environment.CurrentDirectory,
            RedirectStandardInput = options.Input is not null,
        };
        if (OperatingSystem.IsWindows())
        {
            psi.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            psi.Arguments = $"/c {command}";
        }
        else
        {
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
