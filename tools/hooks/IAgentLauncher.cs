namespace ABox.Governance.Hooks;

public interface IAgentLauncher
{
    Task<HookDispatchResult> LaunchAsync(HookManifest manifest, string prompt, HookEvent e, CancellationToken ct);
}
