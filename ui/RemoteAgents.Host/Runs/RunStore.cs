using System.Text.Json;
using System.Text.Json.Serialization;
using RemoteAgents.Runs;

namespace RemoteAgents.Host.Runs;

// Versioned on-disk wrapper for the run index. RunRecord is the single
// durable shape (was: PersistedRun) — RunStatus serializes as its name via
// the converter on RunRecord.Status, so an existing runs.json round-trips.
public sealed record RunsFile(int SchemaVersion, RunRecord[] Runs)
{
    public const int CurrentSchema = 1;
}

// Source-gen context for RunStore's pretty on-disk JSON. Replaces the
// reflection-based JsonSerializer.Deserialize<RunsFile>/Serialize call
// pair the store used to use.
[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RunsFile))]
internal sealed partial class RunStoreJsonContext : JsonSerializerContext { }

// JSON-file-backed persistence at ~/.remote-agents/runs.json. Atomic
// write (write-then-rename) so a power loss can't truncate the file
// halfway. v1 keeps everything in memory and only re-serializes on
// status transitions — fine for the run-counts a single user produces
// in a year (low hundreds). Swap for SQLite when that stops being true.
public sealed class RunStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ILogger<RunStore> _log;

    public RunStore(ILogger<RunStore> log, IConfiguration config)
    {
        _log = log;
        var overrideDir = config["RemoteAgents:RunsDir"];
        var dir = string.IsNullOrWhiteSpace(overrideDir)
            ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".remote-agents")
            : overrideDir;
        Directory.CreateDirectory(dir);
        _path = System.IO.Path.Combine(dir, "runs.json");
    }

    public string FilePath => _path;

    public async Task<RunRecord[]> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return [];
        try
        {
            var json = await File.ReadAllTextAsync(_path, ct);
            var file = JsonSerializer.Deserialize(json, RunStoreJsonContext.Default.RunsFile);
            if (file is null) return [];
            if (file.SchemaVersion != RunsFile.CurrentSchema)
                _log.LogWarning("runs.json schema {Got} != current {Want} — best-effort load", file.SchemaVersion, RunsFile.CurrentSchema);
            return file.Runs ?? [];
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load runs.json — starting empty");
            return [];
        }
    }

    public async Task SaveAsync(IEnumerable<RunRecord> runs, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            // Retention: drop entries older than 90 days. Session dirs on
            // disk are the durable record; runs.json is just the index.
            var cutoff = DateTimeOffset.UtcNow.AddDays(-90);
            var kept = runs
                .Where(r => r.StartedAt > cutoff)
                .OrderByDescending(r => r.StartedAt)
                .ToArray();

            var file = new RunsFile(RunsFile.CurrentSchema, kept);
            var json = JsonSerializer.Serialize(file, RunStoreJsonContext.Default.RunsFile);

            var tmp = _path + ".tmp";
            await File.WriteAllTextAsync(tmp, json, ct);
            File.Move(tmp, _path, overwrite: true);
        }
        finally { _lock.Release(); }
    }
}
