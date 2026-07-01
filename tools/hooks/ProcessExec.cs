using System.Diagnostics;

namespace ABox.Governance.Hooks;

public static class ProcessExec
{
    public static async Task<HookDispatchResult> RunAsync(
        ProcessStartInfo psi, HookManifest manifest, string stdin, int timeoutMs, CancellationToken ct)
    {
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;

        try
        {
            using var proc = new Process { StartInfo = psi };
            if (!proc.Start())
                return new HookDispatchResult(manifest.Path, manifest.Mode, -1, false, "failed to start process", "", "");

            await FeedStdinAsync(proc, stdin);

            // Drain both pipes concurrently from the start: a check hook's stdout is the feedback we
            // relay, and an undrained pipe would deadlock a process that writes more than the buffer holds.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(timeoutMs);
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

    private static async Task FeedStdinAsync(Process proc, string stdin)
    {
        // A process that ignores stdin and exits closes the pipe early; that broken pipe is not a failure.
        try
        {
            await proc.StandardInput.WriteAsync(stdin);
            proc.StandardInput.Close();
        }
        catch (IOException)
        {
        }
    }

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
