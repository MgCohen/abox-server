using RemoteAgents.Host.Sinks;

namespace RemoteAgents.Host.Runs;

public enum RunStatus
{
    Pending = 0,
    Starting,
    Running,
    Completed,
    Failed,
    Canceled,
}

// One in-flight (or recently-finished) flow execution. Identity is the
// server-issued Id (GUID); SessionId is the library's per-run slug that
// names the on-disk session dir. Both are kept so the UI can correlate
// the streaming run with the post-run artifacts.
public sealed class Run
{
    public required Guid Id { get; init; }
    public required string Project { get; init; }
    public required string Flow { get; init; }
    public required string Prompt { get; init; }
    public required string[] Args { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required ChannelSink Sink { get; init; }
    public required CancellationTokenSource Cts { get; init; }

    public RunStatus Status { get; set; } = RunStatus.Pending;
    public string? SessionId { get; set; }
    public string? SessionDir { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public int? ExitCode { get; set; }
    public string? FailureReason { get; set; }
}
