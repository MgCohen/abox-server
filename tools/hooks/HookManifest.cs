namespace ABox.Governance.Hooks;

public sealed record HookManifest(
    string Path,
    IReadOnlyList<HookKind> On,
    HookWhen When,
    HookMode Mode,
    string Run)
{
    public bool Matches(HookEvent e) => On.Contains(e.Kind) && When.Matches(e);
}
