using System.Diagnostics;

namespace ABox.Governance.Hooks;

public sealed class HookRunner
{
    private readonly int _timeoutMs;
    private readonly IAgentLauncher _agent;

    public HookRunner(int timeoutMs = 30_000, IAgentLauncher? agentLauncher = null)
    {
        _timeoutMs = timeoutMs;
        _agent = agentLauncher ?? new ClaudeAgentLauncher();
    }

    public Task<HookDispatchResult> RunAsync(HookManifest manifest, HookEvent e, CancellationToken ct = default) =>
        manifest.Action switch
        {
            HookAction.Agent a => _agent.LaunchAsync(manifest, a.Prompt, e, ct),
            HookAction.Run r => RunShellAsync(manifest, r.Command, e, ct),
            _ => throw new InvalidOperationException($"unhandled hook action {manifest.Action.GetType().Name}"),
        };

    private Task<HookDispatchResult> RunShellAsync(HookManifest manifest, string command, HookEvent e, CancellationToken ct)
    {
        var psi = new ProcessStartInfo { WorkingDirectory = WorkingDir(manifest) };

        // cmd.exe parses its own command line, so the run string must reach it raw via Arguments —
        // ArgumentList would escape embedded quotes/redirects and corrupt the command. A POSIX shell
        // takes the command as a single -c argument. (Mirrors the host's Shell.Command, the proven
        // dialect handling this engine is standalone from.)
        if (OperatingSystem.IsWindows())
        {
            psi.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            psi.Arguments = "/c " + command;
        }
        else
        {
            psi.FileName = "/bin/bash";
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
        }

        return ProcessExec.RunAsync(psi, manifest, e.ToJsonl(), _timeoutMs, ct);
    }

    private static string WorkingDir(HookManifest manifest) =>
        Path.GetDirectoryName(Path.GetFullPath(manifest.Path)) ?? Environment.CurrentDirectory;
}
