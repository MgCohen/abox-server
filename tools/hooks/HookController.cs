namespace ABox.Governance.Hooks;

public sealed class HookController
{
    private readonly HookCatalog _catalog;
    private readonly HookDispatcher _dispatcher;

    public HookController(HookCatalog catalog, HookDispatcher dispatcher)
    {
        _catalog = catalog;
        _dispatcher = dispatcher;
    }

    public async Task<int> DispatchPendingAsync(string logPath, string cursorPath, CancellationToken ct = default)
    {
        var slice = HookLog.ReadSince(logPath, HookCursor.Read(cursorPath));
        if (slice.Events.Count > 0)
        {
            var manifests = _catalog.Scan();
            foreach (var e in slice.Events)
                await _dispatcher.DispatchAsync(e, manifests, ct);
        }

        HookCursor.Write(cursorPath, slice.NextOffset);
        return slice.Events.Count;
    }
}
