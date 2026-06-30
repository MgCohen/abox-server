using System.Diagnostics;

namespace ABox.Governance.Hooks;

public sealed class HookRunner
{
    private readonly int _timeoutMs;

    public HookRunner(int timeoutMs = 30_000)
    {
        _timeoutMs = timeoutMs;
    }

    public async Task<HookDispatchResult> RunAsync(HookManifest manifest, HookEvent e, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = WorkingDir(manifest),
        };

        // cmd.exe parses its own command line, so the run string must reach it raw via
        // Arguments — ArgumentList would escape embedded quotes/redirects and corrupt the
        // command. A POSIX shell takes the command as a single -c argument. (Mirrors the
        // host's Shell.Command, the proven dialect handling this engine is standalone from.)
        if (OperatingSystem.IsWindows())
        {
            psi.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            psi.Arguments = "/c " + manifest.Run;
        }
        else
        {
            psi.FileName = "/bin/bash";
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(manifest.Run);
        }

        try
        {
            using var proc = new Process { StartInfo = psi };
            if (!proc.Start())
                return new HookDispatchResult(manifest.Path, manifest.Mode, -1, false, "failed to start process", "", "");

            await FeedStdinAsync(proc, e);

            // Drain both pipes concurrently from the start: a check hook's stdout is the feedback we
            // relay, and an undrained pipe would deadlock a hook that writes more than the buffer holds.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_timeoutMs);
            try
            {
                await proc.WaitForExitAsync(timeout.Token);
                return new HookDispatchResult(
                    manifest.Path, manifest.Mode, proc.ExitCode, false, null,
                    await stdoutTask, await stderrTask);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                TryKill(proc);
                return new HookDispatchResult(manifest.Path, manifest.Mode, -1, true, null, "", "");
            }
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            return new HookDispatchResult(manifest.Path, manifest.Mode, -1, false, ex.Message, "", "");
        }
    }

    private static async Task FeedStdinAsync(Process proc, HookEvent e)
    {
        // A hook that ignores stdin and exits closes the pipe early; that broken pipe is not a hook failure.
        try
        {
            await proc.StandardInput.WriteAsync(e.ToJsonl());
            proc.StandardInput.Close();
        }
        catch (IOException)
        {
        }
    }

    private static string WorkingDir(HookManifest manifest) =>
        Path.GetDirectoryName(Path.GetFullPath(manifest.Path)) ?? Environment.CurrentDirectory;

    private static void TryKill(Process proc)
    {
        try
        {
            if (!proc.HasExited) proc.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
