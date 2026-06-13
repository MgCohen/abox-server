namespace ABox.Domain.Agents;

public sealed record DriveResult(
    string Text,
    string SessionId,
    int ExitCode,
    string RawOutput,
    IReadOnlyList<AgentTurn> Transcript);
