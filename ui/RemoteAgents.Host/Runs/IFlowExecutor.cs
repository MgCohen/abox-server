namespace RemoteAgents.Host.Runs;

// Pluggable dispatch for a Run. The Host registers one InProcessFlowExecutor
// + one SubprocessFlowExecutor; the FlowRunner consults them in registration
// order and the first CanHandle match wins. In-process is preferred for
// flows registered with FlowCatalog; subprocess is the fallback for flows
// the Host doesn't host directly (e.g. ad-hoc smoke scripts).
public interface IFlowExecutor
{
    bool CanHandle(string flowName);
    Task ExecuteAsync(Run run, CancellationToken ct);
}
