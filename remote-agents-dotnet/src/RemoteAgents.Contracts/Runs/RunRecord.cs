using System.Text.Json.Serialization;

namespace RemoteAgents.Runs;

// Durable snapshot of a flow run. Replaces the previous trio of
// (Host.Run durable fields, PersistedRun, RunSummary) — all three were
// isomorphic projections of the same data.
//
// `Status` is the RunStatus enum + JsonStringEnumConverter so JSON stays
// human-readable ("Running", not "2") while the C# side benefits from
// exhaustive switch / enum-vs-string compiler help.
//
// `ProviderSession` carries Claude's own UUID (or Codex's session id)
// once the agent reports it via AgentEvent.ProviderSessionAttached. Held
// here so the UI / persistence layer can correlate the orchestrator-
// level session with the provider's own artifacts without growing a
// per-provider field on the run.
public sealed record RunRecord(
    Guid                  Id,
    string                Project,
    string                Flow,
    string                Prompt,
    [property: JsonConverter(typeof(JsonStringEnumConverter<RunStatus>))]
    RunStatus             Status,
    DateTimeOffset        StartedAt,
    DateTimeOffset?       EndedAt,
    string?               SessionId,
    string?               SessionDir,
    int?                  ExitCode,
    string?               FailureReason,
    ProviderSessionRef?   ProviderSession = null);
