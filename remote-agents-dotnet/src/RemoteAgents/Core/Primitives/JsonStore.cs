using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace RemoteAgents.Primitives;

// Typed read/modify/write for a JSON file on disk. The orchestrator
// edits small structured files for its own state — task lists, slot
// inventories, run ledgers — and these edits are the classic
// load-mutate-save race: read, mutate in memory, write back. Two
// flows racing on the same file would clobber each other; a process
// kill mid-write would leave half a file.
//
// This primitive folds in both fixes:
//   - Per-absolute-path in-process semaphore around the read+mutate+
//     write sequence.
//   - Save goes through FileOps.AtomicWriteAsync.
//
// Caller passes a JsonTypeInfo<T> from a JsonSerializerContext so the
// whole pipeline is AOT-safe. UpdateAsync's mutator receives null when
// the file doesn't exist yet, letting the caller decide between
// "throw" and "create from defaults."
public static class JsonStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public static async Task<T?> ReadAsync<T>(string path, JsonTypeInfo<T> typeInfo, CancellationToken ct = default)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("JsonStore.ReadAsync: path required", nameof(path));

        if (!File.Exists(path)) return null;

        // No lock on the read path — read is one OS call and any
        // concurrent UpdateAsync writes atomically via rename, so we
        // either see the old bytes or the new bytes.
        var text = await File.ReadAllTextAsync(path, ct);
        if (string.IsNullOrWhiteSpace(text)) return null;
        return JsonSerializer.Deserialize(text, typeInfo);
    }

    public static async Task<T> UpdateAsync<T>(
        string path,
        JsonTypeInfo<T> typeInfo,
        Func<T?, T> mutator,
        CancellationToken ct = default) where T : class
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("JsonStore.UpdateAsync: path required", nameof(path));
        ArgumentNullException.ThrowIfNull(mutator);

        var key = Path.GetFullPath(path);
        var sem = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        try
        {
            var current = await ReadAsync(path, typeInfo, ct);
            var next = mutator(current);
            if (next is null)
                throw new InvalidOperationException(
                    $"JsonStore.UpdateAsync: mutator returned null for {path}; this primitive doesn't delete the file. " +
                    "Delete it explicitly if that's what you want.");

            var json = JsonSerializer.Serialize(next, typeInfo);
            await FileOps.AtomicWriteAsync(path, json, ct);
            return next;
        }
        finally
        {
            sem.Release();
        }
    }
}
