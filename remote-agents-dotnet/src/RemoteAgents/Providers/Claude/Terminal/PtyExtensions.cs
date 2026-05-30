using Porta.Pty;

namespace RemoteAgents.Providers.Claude.Terminal;

public static class PtyExtensions
{
    // Porta.Pty's PtyProcess.ExitCode getter calls Process.ExitCode which
    // throws if the process is still running. Use this wrapper when you
    // don't know for sure whether WaitForExit returned true.
    public static int? ExitCodeOrNull(this IPtyConnection pty)
    {
        try { return pty.ExitCode; }
        catch { return null; }
    }
}
