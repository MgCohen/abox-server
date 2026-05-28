using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteAgents.Events;

// Appends one JSON object per line to a file. Serializes each event by its
// concrete record type so the `kind` field (added below) is consistent.
public sealed class JsonlSink : IEventSink, IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonlSink(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(_path)) File.WriteAllText(_path, "");
    }

    public async Task EmitAsync(AgentEvent evt, CancellationToken ct = default)
    {
        var kind = evt.GetType().Name;
        var line = $"{{\"kind\":{JsonSerializer.Serialize(kind)},{TrimWrappingBraces(JsonSerializer.Serialize(evt, evt.GetType(), JsonOpts))}}}\n";

        await _lock.WaitAsync(ct);
        try
        {
            await File.AppendAllTextAsync(_path, line, Encoding.UTF8, ct);
        }
        finally { _lock.Release(); }
    }

    public void Dispose() => _lock.Dispose();

    private static string TrimWrappingBraces(string json) => json.Substring(1, json.Length - 2);
}
