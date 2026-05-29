using System.Text.Json.Serialization;

namespace RemoteAgents.Flows;

// Reviewer's verdict on the agent's work. Replaces the previous
// stringly-typed Verdict field on CodexVerdict.
//
// Approve  — ship-ready as-is.
// Revise   — needs another pass; details in the review text.
// Unclear  — reviewer didn't return a parseable APPROVE/REVISE prefix.
[JsonConverter(typeof(JsonStringEnumConverter<Verdict>))]
public enum Verdict
{
    Unclear = 0,
    Approve = 1,
    Revise  = 2,
}
