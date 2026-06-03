using System.Text;
using Porta.Pty;

namespace RemoteAgents.Tools.CommandLine;

public sealed class PtySession : IAsyncDisposable, IDisposable
{
    private readonly IPtyConnection _pty;
    private readonly Func<string, CancellationToken, Task>? _onChunk;
    private readonly StringBuilder _buffer = new();
    private readonly object _bufLock = new();
    private readonly CancellationTokenSource _readerCts;
    private readonly CancellationTokenRegistration _killReg;
    private readonly Task _readerTask;
    private DateTimeOffset _lastChunkAt;

    public PtySession(IPtyConnection pty, Func<string, CancellationToken, Task>? onChunk, CancellationToken ct)
    {
        _pty = pty;
        _onChunk = onChunk;
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

    public DateTimeOffset LastChunkAt => _lastChunkAt;

    public int? ExitCode
    {
        get { try { return _pty.ExitCode; } catch { return null; } }
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

    // minWaitMs is a floor for the silent pre-output gap (oracle A4) that a
    // pure idle wait would mistake for a settled TUI.
    public async Task<bool> WaitIdleAsync(
        int idleThresholdMs,
        int maxWaitMs,
        int pollMs = 100,
        int minWaitMs = 0,
        CancellationToken ct = default)
    {
        var entry = DateTimeOffset.UtcNow;
        var deadline = entry.AddMilliseconds(maxWaitMs);
        var floor = entry.AddMilliseconds(minWaitMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(pollMs, ct);
            var now = DateTimeOffset.UtcNow;
            if (now < floor) continue;
            if ((now - _lastChunkAt).TotalMilliseconds > idleThresholdMs) return true;
        }
        return false;
    }

    // Oracle B2: on the happy path the reader is NOT cancelled (that would
    // truncate the final chunk); on the kill path the exit code is forced to
    // -1 because a kill is never a success.
    public async Task<int> ShutdownAsync(int waitForExitMs, int readerDrainMs)
    {
        if (_pty.WaitForExit(waitForExitMs))
        {
            var drained = await Task.WhenAny(_readerTask, Task.Delay(readerDrainMs));
            if (drained != _readerTask)
            {
                _readerCts.Cancel();
                try { await _readerTask; } catch { /* drained best-effort after timeout */ }
            }
            return ExitCode ?? 0;
        }

        KillQuietly();
        _readerCts.Cancel();
        try { await _readerTask; } catch { /* drained best-effort after kill */ }
        var exitCode = ExitCode ?? -1;
        return exitCode == 0 ? -1 : exitCode;
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
                if (_onChunk is not null) await _onChunk(chunk, CancellationToken.None);
            }
        }
        catch (OperationCanceledException) { /* kill-path teardown */ }
        catch (IOException) { /* PTY stream torn down */ }
    }

    private void KillQuietly()
    {
        try { _pty.Kill(); } catch { /* already gone */ }
    }

    public void Dispose()
    {
        _readerCts.Cancel();
        KillQuietly();
        _killReg.Dispose();
        _readerCts.Dispose();
        _pty.Dispose();
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
