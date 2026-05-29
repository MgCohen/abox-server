namespace RemoteAgents.Runs;

// Terminal/transient state of a Run. RunRecord uses this directly; the
// JsonStringEnumConverter on the wire keeps the field human-readable
// in JSON ("Running", not "2"). Add new values to the END to preserve
// numeric stability for any on-disk snapshots.
//
// Interrupted is set by the Host when a Pending/Starting/Running run
// is observed on disk at startup — the previous Host process died with
// the run in flight; the run never reached a real terminal state, but
// it definitely isn't running anymore.
public enum RunStatus
{
    Pending     = 0,
    Starting    = 1,
    Running     = 2,
    Completed   = 3,
    Failed      = 4,
    Canceled    = 5,
    Interrupted = 6,
}
