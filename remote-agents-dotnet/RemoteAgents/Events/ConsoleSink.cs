namespace RemoteAgents.Events;

// Pretty-prints events to stdout. Used during interactive flows so the
// human sees what's happening without tailing the transcript file.
public sealed class ConsoleSink : IEventSink
{
    public Task EmitAsync(AgentEvent evt, CancellationToken ct = default)
    {
        var ts = evt.At.ToLocalTime().ToString("HH:mm:ss");
        switch (evt)
        {
            case AgentEvent.Started s:
                Console.WriteLine($"[{ts}] {s.AgentName} started (session={s.SessionId ?? "new"})");
                break;
            case AgentEvent.StreamChunk c:
                Console.Write(c.Chunk);
                break;
            case AgentEvent.DialogDismissed d:
                Console.WriteLine($"\n[{ts}] {d.AgentName} dismissed dialog ({d.Match})");
                break;
            case AgentEvent.Completed c:
                Console.WriteLine($"\n[{ts}] {c.AgentName} completed (exit={c.ExitCode}, {c.OutputChars} chars)");
                break;
            case AgentEvent.Failed f:
                Console.Error.WriteLine($"\n[{ts}] {f.AgentName} FAILED: {f.Reason}");
                break;
        }
        return Task.CompletedTask;
    }
}
