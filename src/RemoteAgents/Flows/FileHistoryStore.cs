using System.Text.Json;
using RemoteAgents.Contracts;

namespace RemoteAgents.Flows;

public sealed class FileHistoryStore : IHistoryStore
{
    private const int MaxEntries = 50;

    private readonly string _path;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, FlowSnapshot> _byId = [];
    private readonly List<Guid> _order = [];

    public FileHistoryStore()
    {
        // rebuild/ isolates from the quarantined prototype's sibling flows.json; reverts at L12.
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".remote-agents", "rebuild");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "flows.json");
        Load();
    }

    public Task Save(FlowSnapshot snapshot)
    {
        lock (_gate)
        {
            if (!_byId.ContainsKey(snapshot.Id)) _order.Add(snapshot.Id);
            _byId[snapshot.Id] = snapshot;
            while (_order.Count > MaxEntries)
            {
                var evict = _order[0];
                _order.RemoveAt(0);
                _byId.Remove(evict);
            }
            Persist();
        }
        return Task.CompletedTask;
    }

    public FlowSnapshot? Get(Guid id)
    {
        lock (_gate) { return _byId.GetValueOrDefault(id); }
    }

    public IReadOnlyList<FlowSnapshot> Recent()
    {
        lock (_gate) { return [.. _order.Select(id => _byId[id]).Reverse()]; }
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<FlowSnapshot>>(File.ReadAllText(_path), WireJson.Options) ?? [];
            foreach (var s in list)
            {
                if (!_byId.ContainsKey(s.Id)) _order.Add(s.Id);
                _byId[s.Id] = s;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Corrupt or unreadable history is non-fatal — start fresh.
        }
    }

    private void Persist()
    {
        var list = _order.Select(id => _byId[id]).ToList();
        File.WriteAllText(_path, JsonSerializer.Serialize(list, WireJson.Options));
    }
}
