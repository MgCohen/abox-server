namespace RemoteAgents.Agents;

// The cross-provider agent contract. Locked in step 1.5 of the refactor so
// Flow/Step (Workstream A) and the provider rewrites (Workstream D) can
// proceed independently.
//
// Deliberately single-method. Per D9 in PLANS/architecture-refactor/12-rebuild-plan.md,
// "review" is NOT a first-party concept: it's just a step that calls RunAsync
// with a review prompt and parses the returned text into a Verdict (see
// Reviews.ParseVerdict).
public interface IAgent
{
    Task<AgentResult> RunAsync(AgentRunRequest req, CancellationToken ct = default);
}
