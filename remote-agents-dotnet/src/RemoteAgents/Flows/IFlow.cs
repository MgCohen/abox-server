namespace RemoteAgents.Flows;

// A single-shot, addressable, decoratable unit of orchestration. Flows
// are not composed of other flows (see 99-rejected.md R1). Cross-cutting
// decorators implement IFlow and wrap another IFlow; domain preconditions
// live in the flow body, not in decorators (see 99-rejected.md R10).
public interface IFlow
{
    string Name { get; }
    string? Summary { get; }
    Task<FlowResult> RunAsync(FlowContext ctx, FlowArgs args, CancellationToken ct);
}
