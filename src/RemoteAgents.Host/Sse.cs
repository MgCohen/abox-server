using Microsoft.AspNetCore.Http;
using System.Text.Json;
using RemoteAgents.Contracts;

namespace RemoteAgents.Host;

internal static class Sse
{
    public static async Task Stream(HttpContext http, IAsyncEnumerable<FlowSnapshot> snapshots, CancellationToken ct)
    {
        http.Response.Headers.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";

        await foreach (var snap in snapshots.WithCancellation(ct))
        {
            var json = JsonSerializer.Serialize(snap, WireJson.Options);
            await http.Response.WriteAsync($"data: {json}\n\n", ct);
            await http.Response.Body.FlushAsync(ct);
        }
    }
}
