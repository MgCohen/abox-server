using System.Text.Json.Serialization;

namespace RemoteAgents.Sessions;

// Terminal result a flow records on its Session at end. Replaces the
// previous free-form string (`Session.End("shipped" | "no-changes" | ...)`).
// Each enum value names a category of outcome the flow recognized;
// FailureReason carries the human-readable detail.
//
// JsonStringEnumConverter is applied via attribute so on-disk meta.json
// uses the enum's member name. STJ defaults to case-insensitive read,
// so older lowercase entries ("shipped", "failed") still round-trip on
// load.
[JsonConverter(typeof(JsonStringEnumConverter<SessionResult>))]
public enum SessionResult
{
    Ok                      = 0,  // smokes / non-shippable flows
    Shipped                 = 1,
    NoChanges               = 2,
    ValidationFailed        = 3,
    VerdictUnclear          = 4,
    RevisionBrokeValidation = 5,
    AbortedDirtyTree        = 6,
    Failed                  = 7,
}
