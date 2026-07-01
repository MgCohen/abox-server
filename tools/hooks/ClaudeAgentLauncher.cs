using System.Diagnostics;

namespace ABox.Governance.Hooks;

// The dev-loop spawner: fulfills an `agent:` action by running a fresh, minimal-context `claude -p`.
// It is launched HOOK-FREE — ABOX_HOOKS_SUPPRESS=1 in the child's environment makes the spawned
// agent's own turn-end producer a no-op, so a reviewer can never re-trigger the hook that spawned it.
public sealed class ClaudeAgentLauncher : IAgentLauncher
{
    public const string SuppressEnv = "ABOX_HOOKS_SUPPRESS";
    public const string ClaudeBinEnv = "ABOX_HOOKS_CLAUDE";

    private readonly int _timeoutMs;

    public ClaudeAgentLauncher(int timeoutMs = 300_000)
    {
        _timeoutMs = timeoutMs;
    }

    public Task<HookDispatchResult> LaunchAsync(HookManifest manifest, string prompt, HookEvent e, CancellationToken ct)
    {
        var bin = Environment.GetEnvironmentVariable(ClaudeBinEnv);
        bin = string.IsNullOrWhiteSpace(bin) ? "claude" : bin;

        var psi = new ProcessStartInfo
        {
            FileName = bin,
            WorkingDirectory = Directory.Exists(e.Cwd) ? e.Cwd : Environment.CurrentDirectory,
        };
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(prompt);
        psi.Environment[SuppressEnv] = "1";

        return ProcessExec.RunAsync(psi, manifest, "", _timeoutMs, ct);
    }
}
