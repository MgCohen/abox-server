using RemoteAgents.Sessions;

namespace RemoteAgents.Flows;

// Terminal outcome of a flow run. Replaces the previous channel pair
// (Environment.ExitCode + Session.End("string")). A flow returns the
// SessionResult it reached; FlowRunner records it on the session, and the
// CLI shim maps it to a process exit code in exactly one place
// (FlowRunner.MapToExitCode). There is no separate flow-exit enum — the
// run's outcome vocabulary is SessionResult, used end to end.
public sealed record FlowResult(SessionResult Reason, string? Detail = null);
