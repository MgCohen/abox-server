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

    // C2 forward-compat: when the agent emits an AgentQuestion (NeedsInput),
    // the UI POSTs a chosen response here. v1 just records it on the run
    // for inspection — flow-side answer-back wiring is deferred until the
    // library defines the v2 contract (see PLANS/interaction-modes.md Q10).
    public string? PendingQuestionCorrelationId { get; set; }
    public string? PendingResponse { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
}
