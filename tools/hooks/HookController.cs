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

    public async Task<HookDispatchPass> DispatchPendingAsync(string logPath, string cursorPath, CancellationToken ct = default)
    {
        var slice = HookLog.ReadSince(logPath, HookCursor.Read(cursorPath));
        var results = new List<HookDispatchResult>();
        if (slice.Events.Count > 0)
        {
            var manifests = _catalog.Scan();
            foreach (var e in slice.Events)
                results.AddRange(await _dispatcher.DispatchAsync(e, manifests, ct));
        }

        HookCursor.Write(cursorPath, slice.NextOffset);
        return new HookDispatchPass(slice.Events.Count, results);
    }
}
