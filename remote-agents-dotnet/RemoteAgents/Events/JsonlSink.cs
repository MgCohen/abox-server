using System.Text;
using System.Text.Json;
using RemoteAgents.Sessions;

namespace RemoteAgents.Events;

// Appends one JSON object per line to a file. Serializes each event by its
// concrete record type so the `kind` field (added below) is consistent.
public sealed class JsonlSink : IEventSink, IDisposable
{
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
        var body = SerializeEvent(evt);
        var line = $"{{\"kind\":{SerializeKind(kind)},{TrimWrappingBraces(body)}}}\n";

        await _lock.WaitAsync(ct);
        try
        {
            await File.AppendAllTextAsync(_path, line, Encoding.UTF8, ct);
        }
        finally { _lock.Release(); }
    }

    public void Dispose() => _lock.Dispose();

    private static string SerializeEvent(AgentEvent evt) => evt switch
    {
        AgentEvent.Started s         => JsonSerializer.Serialize(s, EventJsonContext.Default.Started),
        AgentEvent.StreamChunk c     => JsonSerializer.Serialize(c, EventJsonContext.Default.StreamChunk),
        AgentEvent.DialogDismissed d => JsonSerializer.Serialize(d, EventJsonContext.Default.DialogDismissed),
        AgentEvent.Completed c       => JsonSerializer.Serialize(c, EventJsonContext.Default.Completed),
        AgentEvent.Failed f          => JsonSerializer.Serialize(f, EventJsonContext.Default.Failed),
        _ => throw new InvalidOperationException($"unknown AgentEvent: {evt.GetType().Name}"),
    };

    private static string SerializeKind(string kind) =>
        JsonSerializer.Serialize(kind, EventJsonContext.Default.String);

    private static string TrimWrappingBraces(string json) => json.Substring(1, json.Length - 2);
}
