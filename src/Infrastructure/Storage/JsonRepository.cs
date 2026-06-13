using System.Text.Json;
using ABox.Infrastructure.Json;

namespace ABox.Infrastructure.Storage;

public sealed class JsonRepository<T> : IRepository<T> where T : IEntity
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<Guid, T>? _entities;

    public JsonRepository(StorageRoot root)
    {
        Directory.CreateDirectory(root.Folder);
        _path = Path.Combine(root.Folder, $"{typeof(T).Name.ToLowerInvariant()}.json");
    }

    public async Task<IReadOnlyList<T>> GetAll(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { return [.. Load().Values]; }
        finally { _gate.Release(); }
    }

    public async Task<T?> GetById(Guid id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { return Load().GetValueOrDefault(id); }
        finally { _gate.Release(); }
    }

    public async Task Add(T entity, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var entities = Load();
            if (!entities.TryAdd(entity.Id, entity))
                throw new InvalidOperationException(
                    $"A {typeof(T).Name} with id {entity.Id} already exists; use Update to replace it.");
            await Persist(entities, ct);
        }
        finally { _gate.Release(); }
    }

    public async Task Update(T entity, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var entities = Load();
            if (!entities.ContainsKey(entity.Id))
                throw new InvalidOperationException(
                    $"No {typeof(T).Name} with id {entity.Id} to update; use Add to create it.");
            entities[entity.Id] = entity;
            await Persist(entities, ct);
        }
        finally { _gate.Release(); }
    }

    public async Task Remove(Guid id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var entities = Load();
            if (entities.Remove(id))
                await Persist(entities, ct);
        }
        finally { _gate.Release(); }
    }

    private Dictionary<Guid, T> Load()
    {
        if (_entities is not null) return _entities;
        if (!File.Exists(_path)) return _entities = [];
        try
        {
            var list = JsonSerializer.Deserialize<List<T>>(File.ReadAllText(_path), WireJson.Options) ?? [];
            return _entities = list.ToDictionary(e => e.Id);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Corrupt or unreadable store is non-fatal — start empty, same posture as FileFlowHistory.
            return _entities = [];
        }
    }

    private async Task Persist(Dictionary<Guid, T> entities, CancellationToken ct)
    {
        var tmp = _path + ".tmp";
        await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(fs, entities.Values.ToList(), WireJson.Options, ct);
            fs.Flush(flushToDisk: true);
        }
        if (File.Exists(_path)) File.Replace(tmp, _path, null);
        else File.Move(tmp, _path);
    }
}
