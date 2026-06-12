using ABox.Domain.Agents;

namespace ABox.Tests.Support;

// Test double for the abstain path: always returns null, so a detected question is
// terminal (the flow surfaces NeedsInput and stops). No production resolver abstains
// — Resolution maps to Auto/Deny/Human — so this lives only in tests.
internal sealed class NonInteractiveResolver : IDecisionResolver
{
    public Resolution Source => Resolution.Human;

    public Task<string?> ResolveAsync(AgentQuestion question, DecisionKind kind, CancellationToken ct)
        => Task.FromResult<string?>(null);
}
