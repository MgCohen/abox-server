using System.Text.Json.Serialization;

namespace RemoteAgents.Events;

// Status of an AgentEvent.Phase emission. Replaces the previous string
// constants on the Phase record. Serialized as the enum member name —
// JsonStringEnumConverter handles read+write; STJ defaults to
// case-insensitive parsing so older lowercase transcript entries
// ("start"/"ok") still round-trip on read.
[JsonConverter(typeof(JsonStringEnumConverter<PhaseStatus>))]
public enum PhaseStatus
{
    Start = 0,
    Ok    = 1,
    Fail  = 2,
    Info  = 3,
}
