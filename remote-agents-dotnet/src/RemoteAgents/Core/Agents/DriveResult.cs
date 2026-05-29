namespace RemoteAgents.Agents;

// Raw output of a provider's DriveAsync — the bytes that came back from
// the CLI, before the base class folds in hook resolution. The base maps
// this onto AgentResult by attaching Status / Question / FailureReason
// derived from <sessionDir>/hooks.jsonl.
public sealed record DriveResult(
    string Text,
    string SessionId,
    int    ExitCode,
    string RawOutput);
