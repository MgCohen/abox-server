namespace RemoteAgents.Agents;

// Raw output of a provider's DriveAsync — the bytes that came back from
// the CLI, before the base class folds in hook resolution. The base maps
// this onto AgentResult by attaching Status / Question / FailureReason.
//
// DetectedQuestion is the escape hatch for providers that surface a
// question through a non-hook channel (e.g. CodexAgent inspecting its
// `-o` output file). The base merges it only when the hooks.jsonl pass
// found nothing — hooks win when both fire.
public sealed record DriveResult(
    string          Text,
    string          SessionId,
    int             ExitCode,
    string          RawOutput,
    AgentQuestion?  DetectedQuestion = null);
