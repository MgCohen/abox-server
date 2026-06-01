using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using RemoteAgents.Contracts;

namespace RemoteAgents.Web.Api;

public sealed class FlowStreamClient(HttpClient http)
{
    public async IAsyncEnumerable<FlowSnapshot> StreamAsync(Guid id, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"flows/{id}/events");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        req.SetBrowserResponseStreamingEnabled(true);

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode == HttpStatusCode.NotFound) yield break;
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
                try { snap = JsonSerializer.Deserialize<FlowSnapshot>(payload, WebJson.Options); }
                catch { /* skip a malformed frame; the next one supersedes it */ }
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
