namespace ABox.Governance.Hooks;

public sealed record HookDispatchPass(int Events, IReadOnlyList<HookDispatchResult> Results)
{
    public static readonly HookDispatchPass Empty = new(0, []);

    public IEnumerable<HookDispatchResult> Checks => Results.Where(r => r.Mode == HookMode.Check);
}
