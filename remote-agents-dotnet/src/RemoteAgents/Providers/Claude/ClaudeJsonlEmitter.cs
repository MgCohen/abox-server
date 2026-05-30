using RemoteAgents.Agents;
using RemoteAgents.Events;

namespace RemoteAgents.Providers.Claude;

// Live-tails Claude's per-session JSONL (~/.claude/projects/<encoded>/<id>.jsonl)
// and emits each parsed line through the supplied IEventSink as AgentEvent
// chat variants. Owned by ClaudeAgent — started from inside DriveAsync the
// moment the session id is known, awaited at turn exit so the final flush
// drains before the sink completes.
//
// Strategy: poll-and-reread. File.ReadAllLines opens + reads + closes
// each tick, so the kernel reports current on-disk length every poll —
// avoids the Windows-FileStream-caches-EOF issue. Partial-line resilience
// holds back any line that hasn't seen its closing `}` yet.
public sealed class ClaudeJsonlEmitter
{
    private readonly string _projectDir;
    private readonly string _sessionId;
    private readonly string _agentName;
    private readonly IEventSink _sink;
    private int _emittedLineCount;

    public ClaudeJsonlEmitter(string projectDir, string sessionId, string agentName, IEventSink sink)
    {
        _projectDir = projectDir;
        _sessionId  = sessionId;
        _agentName  = agentName;
        _sink       = sink;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var path = ClaudeJsonl.PathFor(_projectDir, _sessionId);
        var dirPath = Path.GetDirectoryName(path);
        if (dirPath is not null) Directory.CreateDirectory(dirPath);

        // Wait up to 30s for the file to appear. Claude usually creates it
        // within a couple seconds of TUI launch, but cold starts on a busy
        // box can drift further.
        for (var i = 0; i < 300 && !File.Exists(path) && !ct.IsCancellationRequested; i++)
        {
            try { await Task.Delay(100, ct); }
            catch (OperationCanceledException) { return; }
        }
        if (!File.Exists(path)) return;

        // Poll loop. Each tick re-reads the file (cheap — Claude writes a
        // handful of lines per turn), processes the new tail, then waits.
        // Cancellation exits cleanly — the caller awaits us with a grace
        // window after the PTY closes so the final-flush lines land.
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await ProcessFileAsync(path, ct);
                try { await Task.Delay(150, ct); }
                catch (OperationCanceledException) { break; }
            }
            // Drain pass after cancellation so the final flush isn't lost.
            await ProcessFileAsync(path, CancellationToken.None);
        }
        catch (OperationCanceledException) { /* swallowed: shutdown */ }
    }

    private async Task ProcessFileAsync(string path, CancellationToken ct)
    {
        string[] lines;
        try { lines = await File.ReadAllLinesAsync(path, ct); }
        catch (IOException) { return; }            // transient sharing
        catch (OperationCanceledException) { return; }

        for (var i = _emittedLineCount; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0) { _emittedLineCount++; continue; }

            // Partial-line resilience: don't advance the counter on a line
            // that hasn't reached its closing `}` yet.
            var trimmed = line.TrimEnd();
            if (trimmed.Length == 0 || trimmed[^1] != '}') return;

            _emittedLineCount++;
            foreach (var evt in ClaudeJsonlParser.ParseLine(line, _agentName, DateTimeOffset.UtcNow))
            {
                try { await _sink.EmitAsync(evt, ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
