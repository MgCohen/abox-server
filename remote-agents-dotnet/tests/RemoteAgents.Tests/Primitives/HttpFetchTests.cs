using System.Net;
using System.Text;
using RemoteAgents.Primitives;

namespace RemoteAgents.Tests.Primitives;

public class HttpFetchTests
{
    [Fact]
    public async Task RequestAsync_empty_url_throws()
    {
        var req = new HttpFetchRequest(Url: "");
        await Assert.ThrowsAsync<ArgumentException>(() => HttpFetch.RequestAsync(req));
    }

    [Fact]
    public async Task RequestAsync_relative_url_throws()
    {
        var req = new HttpFetchRequest(Url: "/relative/path");
        await Assert.ThrowsAsync<ArgumentException>(() => HttpFetch.RequestAsync(req));
    }

    [Fact]
    public async Task RequestAsync_GET_reads_body_and_status()
    {
        await using var server = await LocalHttpServer.StartAsync(ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.Headers.Add("X-Test", "hello");
            var bytes = Encoding.UTF8.GetBytes("{\"ok\":true}");
            ctx.Response.ContentType = "application/json";
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        });

        var res = await HttpFetch.RequestAsync(new HttpFetchRequest(server.Url));
        Assert.Equal(200, res.StatusCode);
        Assert.True(res.IsSuccess);
        Assert.Contains("\"ok\":true", res.Body);
        Assert.True(res.Headers.ContainsKey("X-Test"));
        Assert.Equal("hello", res.Headers["X-Test"]);
    }

    [Fact]
    public async Task RequestAsync_POST_sends_body_and_content_type()
    {
        string? receivedBody = null;
        string? receivedContentType = null;
        string? receivedMethod = null;

        await using var server = await LocalHttpServer.StartAsync(ctx =>
        {
            receivedMethod = ctx.Request.HttpMethod;
            receivedContentType = ctx.Request.ContentType;
            using var sr = new StreamReader(ctx.Request.InputStream);
            receivedBody = sr.ReadToEnd();
            ctx.Response.StatusCode = 201;
        });

        await HttpFetch.RequestAsync(new HttpFetchRequest(
            Url: server.Url,
            Method: "POST",
            Body: "{\"x\":1}"));

        Assert.Equal("POST", receivedMethod);
        Assert.Equal("{\"x\":1}", receivedBody);
        Assert.StartsWith("application/json", receivedContentType);
    }

    [Fact]
    public async Task RequestAsync_passes_custom_headers()
    {
        string? auth = null;
        await using var server = await LocalHttpServer.StartAsync(ctx =>
        {
            auth = ctx.Request.Headers["Authorization"];
            ctx.Response.StatusCode = 204;
        });

        await HttpFetch.RequestAsync(new HttpFetchRequest(
            Url: server.Url,
            Headers: new Dictionary<string, string> { ["Authorization"] = "Bearer secret" }));

        Assert.Equal("Bearer secret", auth);
    }

    [Fact]
    public async Task RequestAsync_timeout_throws_TimeoutException()
    {
        await using var server = await LocalHttpServer.StartAsync(ctx =>
        {
            Thread.Sleep(2000);
            ctx.Response.StatusCode = 200;
        });

        await Assert.ThrowsAsync<TimeoutException>(() =>
            HttpFetch.RequestAsync(new HttpFetchRequest(server.Url, TimeoutMs: 250)));
    }
}

// Tiny one-shot HttpListener wrapper. Picks a free port, serves one or
// more requests with the provided handler, dispose stops it.
internal sealed class LocalHttpServer : IAsyncDisposable
{
    public string Url { get; }
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    private LocalHttpServer(string url, HttpListener listener, Action<HttpListenerContext> handler)
    {
        Url = url;
        _listener = listener;
        _loop = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); }
                catch { return; }

                try { handler(ctx); }
                catch { /* swallow — test owns assertions */ }
                finally { try { ctx.Response.Close(); } catch { } }
            }
        });
    }

    public static Task<LocalHttpServer> StartAsync(Action<HttpListenerContext> handler)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var port = Random.Shared.Next(40_000, 50_000);
            var prefix = $"http://127.0.0.1:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(prefix);
            try
            {
                listener.Start();
                return Task.FromResult(new LocalHttpServer(prefix, listener, handler));
            }
            catch (HttpListenerException) { /* port in use, try again */ }
        }
        throw new InvalidOperationException("LocalHttpServer: could not bind a free port after 5 tries");
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        try { _listener.Close(); } catch { }
        try { await _loop.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
    }
}
