using RemoteAgents.Agents;

namespace RemoteAgents.Flows;

// Baseline flow. Claude runs against the requested project, no validation,
// no review, no git.
//
// Native Flow per D5: tools constructor-injected, one Step per
// completion boundary.
public sealed class ClaudeOnlyFlow : Flow
{
    private readonly IAgent _claude;
    private readonly string _projectDir;
    private readonly string _prompt;

    public ClaudeOnlyFlow(IAgent claude, string projectDir, string prompt)
    {
        _claude     = claude;
        _projectDir = projectDir;
        _prompt     = prompt;
    }

    public override string Name => "claude-only";

    protected override Task ExecuteAsync(CancellationToken ct) =>
        AgentStep("claude",
            () => _claude.RunAsync(new AgentRunRequest(_prompt, null, _projectDir), ct));
}
