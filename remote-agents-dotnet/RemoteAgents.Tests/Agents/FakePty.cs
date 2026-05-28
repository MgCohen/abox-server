using System.Text;
using System.Threading.Channels;
using Porta.Pty;

namespace RemoteAgents.Tests.Agents;

// Hand-rolled IPtyConnection double for ClaudeAgent tests. Mimics the
// shape of ConPTY enough that ClaudeAgent's drive loop runs end-to-end
// against scripted reader bytes and a captured writer buffer.
//
// The fake is deliberately simple: no thread-pool gymnastics, no timing
// emulation. Tests that need to assert on the drive loop's timing use
// ClaudeAgentOptions to compress the dwell windows to single-digit ms.
public sealed class FakePtyConnection : IPtyConnection
{
    public Stream ReaderStream { get; }
    public Stream WriterStream { get; }
    public int Pid => 12345;

    public int ExitCode => _exited
        ? _exitCode
        : throw new InvalidOperationException("Process has not exited.");

#pragma warning disable CS0067 // event required by interface, not fired by fake
    public event EventHandler<PtyExitedEventArgs>? ProcessExited;
#pragma warning restore CS0067

    private bool _exited;
    private int _exitCode;
    private readonly ManualResetEventSlim _exitGate = new(initialState: false);

    private readonly _ReaderStream _reader;
    private readonly _WriterStream _writer;

    // What the agent has written (as UTF-8 text), in order.
    public StringBuilder Captured { get; } = new();

    // If non-null, this callback is invoked for every chunk written to
    // WriterStream — used by tests to wire scripted reader responses
    // off of writer events.
    public Action<string>? OnWriteText;

    public FakePtyConnection()
    {
        _reader = new _ReaderStream();
        _writer = new _WriterStream(this);
        ReaderStream = _reader;
        WriterStream = _writer;
    }

    // Queue bytes that the agent will eventually read from ReaderStream.
    public void EnqueueRead(string text) => _reader.Enqueue(Encoding.UTF8.GetBytes(text));

    // Mark process as exited with the given exit code. After this, any
    // pending Read on ReaderStream will see EOF (return 0).
    public void Exit(int exitCode)
    {
        if (_exited) return;
        _exited = true;
        _exitCode = exitCode;
        _reader.CompleteWrites();
        _exitGate.Set();
        // ProcessExited has only an internal constructor in Porta.Pty 1.0.7;
        // ClaudeAgent doesn't subscribe to it, so leaving the event unfired
        // is faithful enough for the agent's drive loop.
    }

    public bool WaitForExit(int milliseconds)
        => _exitGate.Wait(milliseconds);

    public void Kill() => Exit(-1);

    public void Resize(int cols, int rows) { /* no-op */ }

    public void Dispose()
    {
        _exitGate.Dispose();
        _reader.Dispose();
        _writer.Dispose();
    }

    // ── inner streams ─────────────────────────────────────────────────

    private sealed class _ReaderStream : Stream
    {
        private readonly Channel<byte[]> _ch = Channel.CreateUnbounded<byte[]>();
        private byte[]? _residual;
        private int _residualOffset;

        public void Enqueue(byte[] bytes) => _ch.Writer.TryWrite(bytes);
        public void CompleteWrites() => _ch.Writer.TryComplete();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (_residual is null)
            {
                try
                {
                    if (!await _ch.Reader.WaitToReadAsync(ct)) return 0; // EOF
                    if (!_ch.Reader.TryRead(out _residual)) return 0;
                    _residualOffset = 0;
                }
                catch (ChannelClosedException) { return 0; }
            }
            var available = _residual.Length - _residualOffset;
            var toCopy = Math.Min(available, count);
            Buffer.BlockCopy(_residual, _residualOffset, buffer, offset, toCopy);
            _residualOffset += toCopy;
            if (_residualOffset >= _residual.Length) _residual = null;
            return toCopy;
        }
    }

    private sealed class _WriterStream : Stream
    {
        private readonly FakePtyConnection _parent;
        public _WriterStream(FakePtyConnection p) { _parent = p; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            var text = Encoding.UTF8.GetString(buffer, offset, count);
            _parent.Captured.Append(text);
            _parent.OnWriteText?.Invoke(text);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }
    }
}

// Subclass that swaps PtyProvider.SpawnAsync for a caller-supplied
// FakePtyConnection. Used only by tests.
public sealed class TestableClaudeAgent : RemoteAgents.Agents.ClaudeAgent
{
    private readonly FakePtyConnection _fake;
    public TestableClaudeAgent(FakePtyConnection fake) { _fake = fake; }

    protected override Task<IPtyConnection> SpawnPtyAsync(PtyOptions opts, CancellationToken ct)
        => Task.FromResult<IPtyConnection>(_fake);
}
