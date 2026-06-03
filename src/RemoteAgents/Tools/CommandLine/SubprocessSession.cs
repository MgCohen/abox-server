using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace RemoteAgents.Tools.CommandLine;

public sealed class SubprocessSession : IAsyncDisposable
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

    public static SubprocessSession Start(ProcessStartInfo psi, CancellationToken externalCt = default)
    {
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;

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
        _externalReg = externalCt.Register(KillIfRunning);
    }

    public IAsyncEnumerable<string> StdoutLines(CancellationToken ct = default) =>
        _stdout.Reader.ReadAllAsync(ct);

    public IAsyncEnumerable<string> StderrLines(CancellationToken ct = default) =>
        _stderr.Reader.ReadAllAsync(ct);

    public string RawStdout => _rawStdout.ToString();
    public string RawStderr => _rawStderr.ToString();
    public bool TimedOut => _timedOut;
    public bool HasExited => _proc.HasExited;
    public int ExitCode => _proc.HasExited ? _proc.ExitCode : -1;

    public StreamWriter StandardInput => _proc.StandardInput;
    public void CompleteStdin() => _proc.StandardInput.Close();

    public async Task<int> WaitForExitAsync(int timeoutMs, CancellationToken ct = default)
    {
        using var timeoutCts = new CancellationTokenSource(timeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try { await _proc.WaitForExitAsync(linkedCts.Token); }
        catch (OperationCanceledException)
        {
            _timedOut = timeoutCts.IsCancellationRequested;
            KillIfRunning();
            try { await _proc.WaitForExitAsync(CancellationToken.None); }
            catch { /* already exiting; the forced wait only settles final state */ }
            if (!_timedOut) throw;
        }

        // Output callbacks have flushed by the time WaitForExitAsync returns, so the
        // channels can be sealed without dropping a trailing line.
        _stdout.Writer.TryComplete();
        _stderr.Writer.TryComplete();
        return _proc.HasExited ? _proc.ExitCode : -1;
    }

    // Oracle A10 (anti-zombie): anything driving a child process needs a hard-kill
    // guarantee. Kill(entireProcessTree) is the subprocess analog of the PTY Job
    // Object kill-on-close cascade.
    private void KillIfRunning()
    {
        try { if (!_proc.HasExited) _proc.Kill(entireProcessTree: true); }
        catch { /* best-effort: the child may exit between the check and the kill */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _externalReg.Dispose();
        KillIfRunning();
        try { await _proc.WaitForExitAsync(CancellationToken.None); }
        catch { /* best-effort drain on dispose */ }
        _stdout.Writer.TryComplete();
        _stderr.Writer.TryComplete();
        _proc.Dispose();
    }
}
