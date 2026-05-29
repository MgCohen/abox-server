namespace RemoteAgents.Flows;

// Terminal outcome of a flow. Replaces the previous channel pair
// (Environment.ExitCode + Session.End("string")). The CLI dispatcher
// reads Reason and maps to a process exit code in exactly one place.
public sealed record FlowResult(FlowExitReason Reason, string? Detail = null);

public enum FlowExitReason
{
    Shipped                 = 0,
    Ok                      = 1,  // smoke / validate-only flows
    NoChanges               = 2,
    ValidationFailed        = 3,
    VerdictUnclear          = 4,
    RevisionBrokeValidation = 5,
    AbortedDirtyTree        = 6,
    BadArgs                 = 7,
    Failed                  = 8,
}
