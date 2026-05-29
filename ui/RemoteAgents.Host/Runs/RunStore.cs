using System.Text.Json;

namespace RemoteAgents.Host.Runs;

// JSON-file-backed persistence at ~/.remote-agents/runs.json. Atomic
// write (write-then-rename) so a power loss can't truncate the file
// halfway. v1 keeps everything in memory and only re-serializes on
// status transitions — fine for the run-counts a single user produces
// in a year (low hundreds). Swap for SQLite when that stops being true.
public sealed class RunStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

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

    public async Task<PersistedRun[]> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return [];
        try
        {
            var json = await File.ReadAllTextAsync(_path, ct);
            var file = JsonSerializer.Deserialize<RunsFile>(json, JsonOpts);
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

    public async Task SaveAsync(IEnumerable<PersistedRun> runs, CancellationToken ct = default)
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
            var json = JsonSerializer.Serialize(file, JsonOpts);

            var tmp = _path + ".tmp";
            await File.WriteAllTextAsync(tmp, json, ct);
            File.Move(tmp, _path, overwrite: true);
        }
        finally { _lock.Release(); }
    }
}
