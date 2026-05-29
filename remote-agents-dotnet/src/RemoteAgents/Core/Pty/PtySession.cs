using System.Text;
using Porta.Pty;

namespace RemoteAgents.Pty;

// Wraps an IPtyConnection with the boilerplate every PTY-driven agent
// needs: a background reader task that fills a shared buffer and forwards
// chunks to a callback, an idle-detection helper, write helpers, and a
// drain-or-kill shutdown.
//
// ClaudeAgent owns the PTY *script* (when to dwell, when to type the
// prompt, when to send /exit); PtySession owns the *plumbing*. The
// previous inline version mixed buffer locking, reader-task lifecycle,
// and lastChunkAt tracking into ClaudeAgent.DriveAsync — moving them
// here lets DriveAsync read as a sequence of high-level steps.
public sealed class PtySession : IAsyncDisposable, IDisposable
{
    private readonly IPtyConnection _pty;
    private readonly Func<string, CancellationToken, Task>? _onChunk;
    private readonly StringBuilder _buffer = new();
    private readonly object _bufLock = new();
    private readonly CancellationTokenSource _readerCts;
    private readonly Task _readerTask;
    private DateTimeOffset _lastChunkAt;

    // Holds the PTY and starts the background reader immediately.
    //
    // onChunk: invoked for every read chunk (UTF-8 decoded). Receives the
    // session's internal CancellationToken so it tears down with the
    // reader on Kill-path shutdown. Pass null if you don't need chunk
    // notifications.
    //
    // Descendant cleanup: Porta.Pty (Windows) puts the spawned process
    // tree under its own Job Object with kill-on-close, so cmd → claude →
    // (whatever claude spawned) all die when _pty.Dispose() runs. Our
    // job is to make sure _pty.Dispose() actually runs, even on hangs —
    // that's what the cancel-reader-then-kill sequence in DisposeAsync
    // and ClaudeAgent's MaxOverallMs deadline together guarantee.
    public PtySession(
        IPtyConnection pty,
        Func<string, CancellationToken, Task>? onChunk,
        CancellationToken ct)
    {
        _pty = pty;
        _onChunk = onChunk;
        _lastChunkAt = DateTimeOffset.UtcNow;
        _readerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _readerTask = Task.Run(ReadLoopAsync);
    }

    // Snapshot of everything read from the PTY so far.
    public string Buffer
    {
        get { lock (_bufLock) return _buffer.ToString(); }
    }

    // Timestamp of the most recent reader chunk. Used by idle detectors.
    public DateTimeOffset LastChunkAt => _lastChunkAt;

    // Returns the underlying exit code if the process has exited, else null.
    public int? ExitCode
    {
        get { try { return _pty.ExitCode; } catch { return null; } }
    }

    // Write raw bytes to the PTY's stdin and flush. Most callers want
    // WriteLineAsync.
    public async Task WriteAsync(string s, CancellationToken ct = default)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        await _pty.WriterStream.WriteAsync(bytes, 0, bytes.Length, ct);
        await _pty.WriterStream.FlushAsync(ct);
    }

    // Write s followed by \r — matches what a human typing into the TUI
    // would do (cmd.exe and Claude both treat \r as Enter).
    public Task WriteLineAsync(string s, CancellationToken ct = default)
        => WriteAsync(s + "\r", ct);

    // Type text into the PTY, wait `settleMs`, then press Enter.
    // The mid-wait matters for Claude's TUI: pressing Enter while the
    // input buffer is still settling can cause the TUI to treat the
    // input as a bracketed paste (lands in the multi-line buffer
    // instead of submitting). 500ms is the observed safe minimum.
    public async Task SubmitAsync(string text, int settleMs, CancellationToken ct = default)
    {
        await WriteAsync(text, ct);
        await Task.Delay(settleMs, ct);
        await WriteAsync("\r", ct);
    }

    // Spin until the reader has been idle for `idleThresholdMs` or we hit
    // `maxWaitMs`. Polls every `pollMs` (default 100ms — fine-grained
    // enough that scripted callers with small idle thresholds don't
    // pay an extra poll cycle of latency).
    //
    // `minWaitMs` is a floor: idle is not considered "reached" until at
    // least this long has elapsed since entry. Use it when the wait
    // straddles a known pre-output silent gap (e.g. process startup
    // between command echo and first paint) that would otherwise look
    // like a settled TUI. 0 (default) preserves the old behavior.
    //
    // Returns true if the idle threshold was reached, false if maxWait
    // fired first.
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
            if ((now - _lastChunkAt).TotalMilliseconds > idleThresholdMs)
                return true;
        }
        return false;
    }

    // Wait the PTY process out, then give the reader task a grace window
    // to drain trailing bytes. Does NOT cancel the reader on the happy
    // path — that would risk truncating the final chunk. Returns the
    // process exit code, or -1 if it had to be killed.
    //
    // On Kill path, the reader is cancelled, drained best-effort, and the
    // exit code is forced to -1 if the underlying PTY reported 0
    // (Kill is never "success").
    public async Task<int> ShutdownAsync(int waitForExitMs, int readerDrainMs)
    {
        var exited = _pty.WaitForExit(waitForExitMs);
        int exitCode;
        if (exited)
        {
            // Clean EOF path: reader sees ReadAsync==0 when the PTY closes
            // its stream. Wait for it to drain; if it doesn't within the
            // grace window, force cancel so we can return.
            var drained = await Task.WhenAny(_readerTask, Task.Delay(readerDrainMs));
            if (drained != _readerTask)
            {
                _readerCts.Cancel();
                try { await _readerTask; } catch { }
            }
            exitCode = ExitCode ?? 0;
        }
        else
        {
            try { _pty.Kill(); } catch { /* best-effort */ }
            _readerCts.Cancel();
            try { await _readerTask; } catch { }
            exitCode = ExitCode ?? -1;
            if (exitCode == 0) exitCode = -1; // Kill path is never "success"
        }
        return exitCode;
    }

    private async Task ReadLoopAsync()
    {
        var buf = new byte[4096];
        try
        {
            while (true)
            {
                var n = await _pty.ReaderStream.ReadAsync(buf, 0, buf.Length, _readerCts.Token);
                if (n == 0) break; // PTY closed — clean EOF, drained.
                var chunk = Encoding.UTF8.GetString(buf, 0, n);
                lock (_bufLock) _buffer.Append(chunk);
                _lastChunkAt = DateTimeOffset.UtcNow;
                if (_onChunk is not null)
                    await _onChunk(chunk, CancellationToken.None);
            }
        }
        catch (OperationCanceledException) { /* Kill-path teardown */ }
        catch (IOException) { /* PTY stream torn down */ }
    }

    public void Dispose()
    {
        _readerCts.Cancel();
        try { _pty.Kill(); } catch { /* already gone */ }
        _readerCts.Dispose();
        _pty.Dispose();
    }

    // Async disposal. Cancel the reader first so a stuck Read can't pin
    // us, then kill the PTY (best-effort — it may already be dead), then
    // dispose, which closes Porta.Pty's internal Job Object and cascades
    // the kill to every descendant.
    public async ValueTask DisposeAsync()
    {
        _readerCts.Cancel();
        try { _pty.Kill(); } catch { /* already gone */ }
        try { await _readerTask; } catch { }
        _readerCts.Dispose();
        _pty.Dispose();
    }
}
