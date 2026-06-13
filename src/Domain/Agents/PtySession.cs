using System.Text;
using Porta.Pty;

namespace ABox.Domain.Agents;

internal sealed class PtySession : IAsyncDisposable
{
    private readonly IPtyConnection _pty;
    private readonly StringBuilder _buffer = new();
    private readonly object _bufLock = new();
    private readonly CancellationTokenSource _readerCts;
    private readonly CancellationTokenRegistration _killReg;
    private readonly Task _readerTask;
    private DateTimeOffset _lastChunkAt;

    public PtySession(IPtyConnection pty, CancellationToken ct)
    {
        _pty = pty;
        _lastChunkAt = DateTimeOffset.UtcNow;
        _readerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Oracle A10: the kill-on-hang guarantee must hold even if the caller
        // never reaches DisposeAsync — cancelling ct kills the tree directly.
        _killReg = ct.Register(KillQuietly);
        _readerTask = Task.Run(ReadLoopAsync);
    }

    public string Buffer
    {
        get { lock (_bufLock) return _buffer.ToString(); }
    }

    public async Task WriteAsync(string s, CancellationToken ct = default)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        await _pty.WriterStream.WriteAsync(bytes, 0, bytes.Length, ct);
        await _pty.WriterStream.FlushAsync(ct);
    }

    public Task WriteLineAsync(string s, CancellationToken ct = default) => WriteAsync(s + "\r", ct);

    // Oracle A5: a pause between typing and Enter stops Claude's TUI treating
    // the input as a bracketed paste instead of a submit.
    public async Task SubmitAsync(string text, int settleMs, CancellationToken ct = default)
    {
        await WriteAsync(text, ct);
        await Task.Delay(settleMs, ct);
        await WriteAsync("\r", ct);
    }

    // Wait for a positive readiness signal: the predicate holds over the
    // buffer AND no new bytes have arrived for settleMs (so a mid-render lull
    // can't be mistaken for a settled UI). Returns false if maxWait fired first.
    public async Task<bool> WaitUntilAsync(
        Func<string, bool> predicate,
        int settleMs,
        int maxWaitMs,
        int pollMs = 100,
        CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(maxWaitMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(pollMs, ct);
            if (!predicate(Buffer)) continue;
            if ((DateTimeOffset.UtcNow - _lastChunkAt).TotalMilliseconds >= settleMs) return true;
        }
        return false;
    }

    private async Task ReadLoopAsync()
    {
        var buf = new byte[4096];
        try
        {
            while (true)
            {
                var n = await _pty.ReaderStream.ReadAsync(buf, 0, buf.Length, _readerCts.Token);
                if (n == 0) break;
                var chunk = Encoding.UTF8.GetString(buf, 0, n);
                lock (_bufLock) _buffer.Append(chunk);
                _lastChunkAt = DateTimeOffset.UtcNow;
            }
        }
        catch (OperationCanceledException) { /* kill-path teardown */ }
        catch (IOException) { /* PTY stream torn down */ }
    }

    private void KillQuietly()
    {
        try { _pty.Kill(); } catch { /* already gone */ }
    }

    // Cancel the reader first so a stuck Read can't pin us, then kill the PTY,
    // then dispose — which closes Porta.Pty's Job Object and cascades the kill
    // to every descendant (oracle A10).
    public async ValueTask DisposeAsync()
    {
        _readerCts.Cancel();
        KillQuietly();
        try { await _readerTask; } catch { /* teardown drain */ }
        _killReg.Dispose();
        _readerCts.Dispose();
        _pty.Dispose();
    }
}
