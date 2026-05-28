using System.Diagnostics;
using System.Text;

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
    long DurationMs);

public static class RunCommand
{
    public static async Task<RunCommandResult> RunAsync(
        string command,
        RunCommandOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new RunCommandOptions();
        var sw = Stopwatch.StartNew();

        // Windows-only v1: always go through cmd.exe /c so users can write
        // pipes / chains in their validators (matches JS shell:true default).
        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Arguments = $"/c {command}",
            WorkingDirectory = options.Cwd ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = options.Input is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        ApplyEnv(psi, options.Env);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        if (options.Input is not null)
        {
            await proc.StandardInput.WriteAsync(options.Input);
            proc.StandardInput.Close();
        }

        using var timeoutCts = new CancellationTokenSource(options.TimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var timedOut = false;
        try
        {
            await proc.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = timeoutCts.IsCancellationRequested;
            try { proc.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            try { await proc.WaitForExitAsync(CancellationToken.None); } catch { }
            if (!timedOut) throw;
        }

        return new RunCommandResult(
            Command: command,
            ExitCode: proc.HasExited ? proc.ExitCode : -1,
            Stdout: stdout.ToString(),
            Stderr: stderr.ToString(),
            TimedOut: timedOut,
            DurationMs: sw.ElapsedMilliseconds);
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
