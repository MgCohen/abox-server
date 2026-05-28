using System.Text;
using System.Text.Json;
using RemoteAgents.Sessions;

namespace RemoteAgents.Events;

// Appends one JSON object per line to a file. Holds the writer open for
// the life of the sink — flow runs emit hundreds of StreamChunk events,
// so reopening the file per write is a real cost.
//
// The "kind" discriminator is injected by AgentEvent's [JsonPolymorphic]
// attribute, not by string-splicing here.
public sealed class JsonlSink : IEventSink, IAsyncDisposable, IDisposable
{
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonlSink(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            NewLine = "\n",
        };
    }

    public async Task EmitAsync(AgentEvent evt, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(evt, EventJsonContext.Default.AgentEvent);
        await _lock.WaitAsync(ct);
        try
        {
            await _writer.WriteLineAsync(json.AsMemory(), ct);
            await _writer.FlushAsync(ct);
        }
        finally { _lock.Release(); }
    }

    public void Dispose()
    {
        _writer.Dispose();
        _lock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        _lock.Dispose();
    }
}
