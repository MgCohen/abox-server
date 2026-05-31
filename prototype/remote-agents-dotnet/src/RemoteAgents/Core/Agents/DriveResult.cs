namespace RemoteAgents.Agents;

// Raw output of a provider's DriveAsync — the bytes that came back from
// the CLI, before the base class folds in hook resolution. The base maps
// this onto AgentResult by attaching Status / Question / FailureReason
// derived from <sessionDir>/hooks.jsonl.
//
// Transcript is the ordered turn list the provider extracted from its
// native JSONL/event stream (full fidelity, no truncation). Empty array
// when the provider couldn't (or chose not to) materialize one — the
// base class forwards it as-is.
public sealed record DriveResult(
    string       Text,
    string       SessionId,
    int          ExitCode,
    string       RawOutput,
    AgentTurn[]? Transcript = null);
