using RemoteAgents.Agents;
using RemoteAgents.Events;
using RemoteAgents.Validation;

namespace RemoteAgents.Flows;

public sealed record ValidateAndFixResult(bool Ok, AgentResult LastResult, ValidationResult LastValidation);

// Run validator; if it fails, ask the agent to fix it (resuming the same
// session); repeat up to maxAttempts. Returns the final agent result and
// whether validation eventually passed.
//
// progressNote: optional suffix on "[validate] attempt N..." for slow
// validators ("(Unity batch-mode, this can take minutes)").
//
// fixDescriptor: optional parenthetical in the fix prompt sent back to the
// agent — empty → "Validation failed."; "Unity batch-mode compile" →
// "Validation (Unity batch-mode compile) failed."
public static class Loops
{
    public static async Task<ValidateAndFixResult> ValidateAndFixAsync(
        Agent agent,
        IValidator validator,
        AgentResult initialResult,
        string projectDir,
        IEventSink sink,
        int maxAttempts = 3,
        string progressNote = "",
        string fixDescriptor = "",
        CancellationToken ct = default)
    {
        var last = new ValidationResult(false, "", "");
        var result = initialResult;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await sink.PhaseStartAsync("validate", $"attempt {attempt}{progressNote}...", ct);
            last = await validator.ValidateAsync(projectDir, ct);
            if (last.Ok)
            {
                await sink.PhaseOkAsync("validate", $"PASSED — {last.Summary}", ct);
                return new ValidateAndFixResult(true, result, last);
            }
            await sink.PhaseFailAsync("validate", $"FAILED — {last.Summary}", ct);
            if (attempt == maxAttempts) break;

            var descriptor = string.IsNullOrEmpty(fixDescriptor) ? "" : $" ({fixDescriptor})";
            var fixPrompt = $"Validation{descriptor} failed. Address these issues:\n\n{last.Errors}";
            result = await agent.RunAsync(new AgentRunRequest(fixPrompt, result.SessionId, projectDir), ct);
            await sink.PhaseOkAsync(agent.Name, $"fix turn {attempt + 1} done", ct);
        }

        await sink.PhaseFailAsync("abort", $"validation never passed after {maxAttempts} attempts.", ct);
        return new ValidateAndFixResult(false, result, last);
    }
}
