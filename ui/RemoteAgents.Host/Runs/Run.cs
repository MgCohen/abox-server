using RemoteAgents.Host.Sinks;
using RemoteAgents.Runs;

namespace RemoteAgents.Host.Runs;

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

    // Absolute path of the project the flow runs against. Resolved by
    // ProjectRegistry when the run starts. In-process executor needs it
    // to construct the FlowContext.
    public string? ProjectDir { get; set; }

    public RunStatus Status { get; set; } = RunStatus.Pending;
    public string? SessionId { get; set; }
    public string? SessionDir { get; set; }

    // Claude's own session UUID, sniffed from the cmd.exe echo of the
    // `claude --session-id <uuid> ...` launch line by the subprocess
    // executor. Deleted in Phase 6 step 4 — the provider session id
    // arrives via AgentEvent.ProviderSessionAttached now (see
    // ProviderSession below).
    public string? ClaudeSessionId { get; set; }

    // Populated by RunStateSink when an AgentEvent.ProviderSessionAttached
    // event flows through the run's sink. The single source of truth for
    // the provider-side session ref the UI surfaces via RunRecord.
    public ProviderSessionRef? ProviderSession { get; set; }

    public DateTimeOffset? EndedAt { get; set; }
    public int? ExitCode { get; set; }
    public string? FailureReason { get; set; }

    // SubprocessFlowExecutor internal: the transcript tailer task, started
    // when the session-id line is sniffed from child stdout. ExecuteAsync
    // awaits this AFTER setting EndedAt so the tailer's "is the run done?"
    // exit condition can fire.
    public Task? TailerTask { get; set; }

    // C2 forward-compat: when the agent emits an AgentQuestion (NeedsInput),
    // the UI POSTs a chosen response here. v1 just records it on the run
    // for inspection — flow-side answer-back wiring is deferred until the
    // library defines the v2 contract (see PLANS/interaction-modes.md Q10).
    public string? PendingQuestionCorrelationId { get; set; }
    public string? PendingResponse { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
}
