using System.Text.Json.Serialization;

namespace RemoteAgents.Flows;

// Normalized step lifecycle (D6): every step transitions itself through
// these states. trust/bypass and other provider micro-state are normalized
// to Completed/Paused/Failed inside the adapter before they escape (D10).
[JsonConverter(typeof(JsonStringEnumConverter<StepStatus>))]
public enum StepStatus { Pending, Running, Paused, Completed, Failed, Canceled }

// Flow phase mirrors step status at the aggregate level.
[JsonConverter(typeof(JsonStringEnumConverter<FlowPhase>))]
public enum FlowPhase  { Pending, Running, Paused, Completed, Failed, Canceled }

// Wire DTO for one step. The "event list for display" is just an array of
// these inside FlowSnapshot — no separate events channel (D2).
public sealed record StepDto(
    string             Name,
    StepStatus         Status,
    DateTimeOffset     StartedAt,
    DateTimeOffset?    EndedAt,
    string?            Summary,
    string?            Error);

// One atomic read of a flow's full state. Versioned so REST can ETag and
// SSE can coalesce (D1+D2+D3). The UI never holds anything but a sequence
// of these.
public sealed record FlowSnapshot(
    Guid               Id,
    string             Name,
    FlowPhase          Phase,
    long               Version,
    string?            PendingQuestion,
    StepDto[]          Steps);
