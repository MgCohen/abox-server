using System.Text.Json;
using RemoteAgents.Flows;

namespace RemoteAgents.Hosting;

// JSON-file-backed IHistoryStore. Atomic write (tmp + rename) so a power
// loss can't truncate. In-memory list mirror; persisted on every Save.
// 90-day retention matches the old RunStore.
//
// Default path is ~/.remote-agents/flows.json; the constructor takes an
// override so tests / non-default deploys can point elsewhere.
public sealed class FileHistoryStore : IHistoryStore
{
    private const int RetentionDays = 90;

    private readonly string _path;
    private readonly object _lock = new();
    private List<FlowSnapshot> _all;

    public FileHistoryStore(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _all = Load();
    }

    public string FilePath => _path;

    public void Save(FlowSnapshot snap)
    {
        lock (_lock)
        {
            _all.RemoveAll(s => s.Id == snap.Id);
            _all.Insert(0, snap);
            Persist();
        }
    }

    public FlowSnapshot? Get(Guid id)
    {
        lock (_lock) return _all.FirstOrDefault(s => s.Id == id);
    }

    public IReadOnlyList<FlowSnapshot> Recent()
    {
        lock (_lock) return _all.ToList();
    }

    private List<FlowSnapshot> Load()
    {
        if (!File.Exists(_path)) return new();
        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize(json, FlowJsonContext.Default.ListFlowSnapshot) ?? new();
        }
        catch { return new(); }
    }

    private void Persist()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-RetentionDays);
        var kept = _all
            .Where(s => s.Steps.Length == 0 || (s.Steps[0].StartedAt > cutoff))
            .OrderByDescending(s => s.Steps.Length == 0 ? DateTimeOffset.MinValue : s.Steps[0].StartedAt)
            .ToList();
        _all = kept;
        var json = JsonSerializer.Serialize(_all, FlowJsonContext.Default.ListFlowSnapshot);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);
    }

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".remote-agents", "flows.json");
}
