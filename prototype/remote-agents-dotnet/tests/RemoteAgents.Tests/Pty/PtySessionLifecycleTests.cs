using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using Porta.Pty;
using RemoteAgents.Providers.Claude.Terminal;

namespace RemoteAgents.Tests.Pty;

// Proves the bug that left JS-prototype zombies for weeks can't happen
// in the C# port: when PtySession is disposed, the PTY's grandchildren
// die too. Without the Job Object, cmd.exe gets killed but a ping
// grandchild survives — exactly the orphan pattern we saw in PIDs
// 3756/11520/42960.
//
// Windows-only by design; non-Windows runs return early (xUnit reports
// pass, no skip noise — orchestrator is Windows-only in v1 anyway).
public class PtySessionLifecycleTests
{
    [Fact]
    public async Task Disposing_session_terminates_pty_and_descendants()
    {
        if (!OperatingSystem.IsWindows()) return;

        var pty = await PtyProvider.SpawnAsync(new PtyOptions
        {
            Name = "lifecycle-test",
            Cols = 80,
            Rows = 25,
            Cwd = Path.GetTempPath(),
            App = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Environment = CaptureEnv(),
        }, CancellationToken.None);

        var ptyPid = pty.Pid;

        int? childPid;
        await using (var session = new PtySession(pty, onChunk: null, CancellationToken.None))
        {
            // Spawn a long-lived grandchild from inside the PTY shell.
            // ping -n 99 127.0.0.1 sits ~99 seconds — well past our 2s
            // assertion window.
            await session.WriteLineAsync("ping -n 99 127.0.0.1");

            // Wait up to 5s for the ping grandchild to appear under cmd.
            childPid = await WaitForChildAsync(ptyPid, "PING.EXE", TimeSpan.FromSeconds(5));
            Assert.NotNull(childPid);
            Assert.True(IsAlive(ptyPid),    $"PTY pid {ptyPid} should still be alive before dispose.");
            Assert.True(IsAlive(childPid!.Value), $"Child pid {childPid} should still be alive before dispose.");
        }

        // Job Object kill-on-close fires synchronously on handle close,
        // but process termination is a kernel callback — give it up to 2s.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline && (IsAlive(ptyPid) || IsAlive(childPid!.Value)))
            await Task.Delay(50);

        Assert.False(IsAlive(ptyPid),         $"PTY pid {ptyPid} survived dispose.");
        Assert.False(IsAlive(childPid!.Value), $"Grandchild pid {childPid} survived dispose — Job Object isn't reaping descendants.");
    }

    // ───────────────────────────────────────────────────────────────────

    private static bool IsAlive(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static async Task<int?> WaitForChildAsync(int parentPid, string childExeName, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var query = $"SELECT ProcessId, Name FROM Win32_Process WHERE ParentProcessId = {parentPid}";
            using var searcher = new ManagementObjectSearcher(query);
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                {
                    var name = (string)mo["Name"];
                    if (string.Equals(name, childExeName, StringComparison.OrdinalIgnoreCase))
                        return Convert.ToInt32(mo["ProcessId"]);
                }
            }
            await Task.Delay(100);
        }
        return null;
    }

    private static Dictionary<string, string> CaptureEnv()
    {
        var env = new Dictionary<string, string>();
        foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
            if (kv.Key is string k && kv.Value is string v) env[k] = v;
        return env;
    }
}
