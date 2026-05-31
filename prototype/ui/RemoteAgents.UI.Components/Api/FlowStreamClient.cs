using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using RemoteAgents.Flows;

namespace RemoteAgents.UI.Components.Api;

// Minimal SSE client for /flows/{id}/events. Each `data: ...` block
// deserializes to a FlowSnapshot; the snapshot's Version is monotonic, so
// the client can ignore the absence of Last-Event-ID replay (v1 non-goal —
// coalesce-to-latest semantics on the server side mean the latest snapshot
// is always sufficient).
public sealed class FlowStreamClient
{
    private readonly HttpClient _http;

    public FlowStreamClient(HttpClient http) => _http = http;

    public async IAsyncEnumerable<FlowSnapshot> StreamAsync(Guid id, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"flows/{id}/events");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) yield break;
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var data = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            string? line;
            try { line = await reader.ReadLineAsync(ct); }
            catch (OperationCanceledException) { yield break; }
            if (line is null) break;

            if (line.Length == 0)
            {
                if (data.Length == 0) continue;
                var payload = data.ToString();
                data.Clear();
                FlowSnapshot? snap = null;
                try { snap = JsonSerializer.Deserialize(payload, FlowJsonContext.Default.FlowSnapshot); }
                catch { /* skip malformed frame */ }
                if (snap is not null) yield return snap;
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(line, 6, line.Length - 6);
            }
        }
    }
}
