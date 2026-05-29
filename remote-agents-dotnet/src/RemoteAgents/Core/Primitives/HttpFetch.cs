using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;

namespace RemoteAgents.Primitives;

public sealed record HttpFetchRequest(
    string Url,
    string Method = "GET",
    IReadOnlyDictionary<string, string>? Headers = null,
    // Request body. When non-null and Headers doesn't set Content-Type,
    // ContentType is used (default application/json — the common case).
    string? Body = null,
    string ContentType = "application/json",
    int TimeoutMs = 30_000);

public sealed record HttpFetchResult(
    int StatusCode,
    IReadOnlyDictionary<string, string> Headers,
    string Body,
    long DurationMs)
{
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
}

// Minimal HTTP wrapper. One shared HttpClient (connection pooling +
// DNS caching), per-request timeout via a linked CTS so the shared
// client's lifetime isn't affected. Headers are case-insensitive on
// the way out.
//
// This is the "talk to an API and read the payload" primitive — not a
// general-purpose REST framework. Streaming, multipart, retries, and
// auth are out of scope; callers compose them.
public static class HttpFetch
{
    private static readonly HttpClient _client = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = System.Net.DecompressionMethods.All,
    })
    {
        // Per-request timeout takes precedence via linked CTS; this is
        // the upper bound for the shared client.
        Timeout = TimeSpan.FromMinutes(10),
    };

    public static async Task<HttpFetchResult> RequestAsync(HttpFetchRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Url))
            throw new ArgumentException("HttpFetch: url required", nameof(req));
        if (!Uri.TryCreate(req.Url, UriKind.Absolute, out var uri))
            throw new ArgumentException($"HttpFetch: not an absolute URL: {req.Url}", nameof(req));

        using var msg = new HttpRequestMessage(new HttpMethod(req.Method), uri);

        if (req.Body is not null)
        {
            msg.Content = new StringContent(req.Body, Encoding.UTF8);
            // Caller can override Content-Type via Headers below; we
            // set our default first.
            msg.Content.Headers.ContentType = new MediaTypeHeaderValue(req.ContentType);
        }

        if (req.Headers is not null)
        {
            foreach (var (k, v) in req.Headers)
            {
                // Content-* headers belong on the content; everything
                // else on the request. TryAddWithoutValidation lets
                // callers send non-standard pairs without us policing.
                if (k.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                {
                    if (msg.Content is null) msg.Content = new StringContent(""); // edge: header without body
                    msg.Content.Headers.Remove(k);
                    msg.Content.Headers.TryAddWithoutValidation(k, v);
                }
                else
                {
                    msg.Headers.TryAddWithoutValidation(k, v);
                }
            }
        }

        using var timeoutCts = new CancellationTokenSource(req.TimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var sw = Stopwatch.StartNew();
        HttpResponseMessage resp;
        try
        {
            resp = await _client.SendAsync(msg, HttpCompletionOption.ResponseContentRead, linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            throw new TimeoutException($"HttpFetch: request to {req.Url} timed out after {req.TimeoutMs}ms");
        }

        var body = await resp.Content.ReadAsStringAsync(linkedCts.Token);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in resp.Headers) headers[h.Key] = string.Join(", ", h.Value);
        foreach (var h in resp.Content.Headers) headers[h.Key] = string.Join(", ", h.Value);

        var result = new HttpFetchResult(
            StatusCode: (int)resp.StatusCode,
            Headers: headers,
            Body: body,
            DurationMs: sw.ElapsedMilliseconds);

        resp.Dispose();
        return result;
    }
}
