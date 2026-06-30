namespace ABox.Governance.Hooks;

public sealed class HookDispatcher
{
    private readonly HookRunner _runner;

    public HookDispatcher(HookRunner runner)
    {
        _runner = runner;
    }

    public async Task<IReadOnlyList<HookDispatchResult>> DispatchAsync(
        HookEvent e, IReadOnlyList<HookManifest> manifests, CancellationToken ct = default)
    {
        var matches = manifests.Where(m => m.Mode == HookMode.React && m.Matches(e));
        return await Task.WhenAll(matches.Select(m => _runner.RunAsync(m, e, ct)));
    }
}
