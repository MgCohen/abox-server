using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace RemoteAgents.Pty;

// Subprocess peer to PtySession. Owns the boilerplate every direct-
// subprocess driver was reinventing: spawn, stdout/stderr pump (into
// both an accumulator and a channel for streaming consumers), stdin
// writer, linked-CTS timeout, Kill(entireProcessTree) on cancel/timeout.
//
// CodexAgent (script + session-id sniff), RunCommand (buffered facade),
// and the Host's SubprocessFlowExecutor (streaming line consumer) all
// drive a process through this so the transport plumbing lives in one
// place. PtySession stays the PTY-specific variant — both expose the
// same shape: an awaitable wait + a chunk stream + drain-on-dispose.
public sealed class SubprocessSession : IAsyncDisposable, IDisposable
{
    private readonly Process _proc;
    private readonly Channel<string> _stdout = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly Channel<string> _stderr = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
    private readonly StringBuilder _rawStdout = new();
    private readonly StringBuilder _rawStderr = new();
    private readonly CancellationTokenRegistration _externalReg;
    private bool _timedOut;
    private bool _disposed;

    // Builds the session on a caller-supplied PSI. Redirects + non-shell
    // are forced so the process is fully owned by us. Stdin is only
    // redirected if the caller already asked for it (codex pipes prompt
    // via stdin; the shell wrappers don't).
    public static SubprocessSession Start(ProcessStartInfo psi, CancellationToken externalCt = default)
    {
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError  = true;
        psi.UseShellExecute        = false;
        psi.CreateNoWindow         = true;

        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var session = new SubprocessSession(proc, externalCt);

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            session._rawStdout.AppendLine(e.Data);
            session._stdout.Writer.TryWrite(e.Data);
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            session._rawStderr.AppendLine(e.Data);
            session._stderr.Writer.TryWrite(e.Data);
        };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        return session;
    }

    private SubprocessSession(Process proc, CancellationToken externalCt)
    {
        _proc = proc;
        // External cancellation: kill the subtree on the way out. The
        // process owns its child tree, so Kill(entireProcessTree) is the
        // only safe teardown when the caller's CT fires mid-flight.
        _externalReg = externalCt.Register(KillIfRunning);
    }

    // Lines as they come, in order. Completes once the process exits and
    // the OutputDataReceived pump has drained. Each enumerator should
    // be consumed by exactly one reader (Channel(SingleReader=true)).
    public IAsyncEnumerable<string> StdoutLines(CancellationToken ct = default) =>
        _stdout.Reader.ReadAllAsync(ct);

    public IAsyncEnumerable<string> StderrLines(CancellationToken ct = default) =>
        _stderr.Reader.ReadAllAsync(ct);

    // Everything written to stdout/stderr so far, in arrival order.
    public string RawStdout => _rawStdout.ToString();
    public string RawStderr => _rawStderr.ToString();

    // WaitForExitAsync timed out (vs. completing normally or being
    // externally cancelled). Set only by the wait method.
    public bool TimedOut => _timedOut;

    public bool HasExited => _proc.HasExited;
    public int ExitCode => _proc.HasExited ? _proc.ExitCode : -1;

    // Caller writes the prompt (or whatever input the child reads from
    // stdin) and then calls CompleteStdin to flush + close.
    public StreamWriter StandardInput => _proc.StandardInput;
    public void CompleteStdin() => _proc.StandardInput.Close();

    // Waits up to timeoutMs for the process to exit. On timeout, sets
    // TimedOut and kills the subtree, then awaits exit so caller sees a
    // finished process. External CT cancellation (or `ct`) kills + rethrows
    // OperationCanceledException so the caller knows it was a hard abort.
    public async Task<int> WaitForExitAsync(int timeoutMs, CancellationToken ct = default)
    {
        using var timeoutCts = new CancellationTokenSource(timeoutMs);
        using var linkedCts  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try { await _proc.WaitForExitAsync(linkedCts.Token); }
        catch (OperationCanceledException)
        {
            _timedOut = timeoutCts.IsCancellationRequested;
            KillIfRunning();
            try { await _proc.WaitForExitAsync(CancellationToken.None); } catch { }
            if (!_timedOut) throw;
        }

        // Stdout/Stderr callbacks finish before WaitForExitAsync returns,
        // so it's safe to seal the channels — consumers iterating
        // StdoutLines/StderrLines now see clean completion.
        _stdout.Writer.TryComplete();
        _stderr.Writer.TryComplete();
        return _proc.HasExited ? _proc.ExitCode : -1;
    }

    private void KillIfRunning()
    {
        try { if (!_proc.HasExited) _proc.Kill(entireProcessTree: true); }
        catch { /* best-effort */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _externalReg.Dispose();
        KillIfRunning();
        try { _proc.WaitForExit(2_000); } catch { }
        _stdout.Writer.TryComplete();
        _stderr.Writer.TryComplete();
        _proc.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _externalReg.Dispose();
        KillIfRunning();
        try { await _proc.WaitForExitAsync(CancellationToken.None); } catch { }
        _stdout.Writer.TryComplete();
        _stderr.Writer.TryComplete();
        _proc.Dispose();
    }
}
