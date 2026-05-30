using RemoteAgents.Agents;
using RemoteAgents.Validation;

namespace RemoteAgents.Flows;

// Claude does the work → validator runs → if it fails, resume the Claude
// session and feed the failure back → retry up to MaxFixAttempts. No
// review, no auto-commit.
//
// Ported from the prototype's cli/flows/claude-validate.cs.
public sealed class ClaudeValidateFlow : Flow
{
    private const int MaxFixAttempts = 3;

    private readonly IAgent     _claude;
    private readonly IValidator _validator;
    private readonly string     _projectDir;
    private readonly string     _prompt;

    public ClaudeValidateFlow(IAgent claude, IValidator validator, string projectDir, string prompt)
    {
        _claude     = claude;
        _validator  = validator;
        _projectDir = projectDir;
        _prompt     = prompt;
    }

    public override string Name => "claude-validate";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var work = await Step("claude",
            () => _claude.RunAsync(new AgentRunRequest(_prompt, null, _projectDir), ct));

        for (var attempt = 1; attempt <= MaxFixAttempts; attempt++)
        {
            var v = await Step($"validate-{attempt}",
                () => _validator.ValidateAsync(_projectDir, ct));
            if (v.Ok) return;
            if (attempt == MaxFixAttempts)
                throw new InvalidOperationException(
                    $"Validation never passed after {MaxFixAttempts} attempts: {v.Summary}");

            var fixPrompt = $"Validation failed. Address these issues:\n\n{v.Errors}";
            work = await Step($"fix-{attempt}",
                () => _claude.RunAsync(new AgentRunRequest(fixPrompt, work.SessionId, _projectDir), ct));
        }
    }
}
