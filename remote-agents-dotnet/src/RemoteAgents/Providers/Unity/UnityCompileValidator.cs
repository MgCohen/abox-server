using RemoteAgents.Agents;
using RemoteAgents.Validation;

namespace RemoteAgents.Providers.Unity;

// Fast preflight: Unity launched in batch-mode with -quit. Catches
// syntax errors and missing references in ~30s + actual compile time
// without running any tests. Use when the goal is "did the LLM break
// the build?" and the fix loop should iterate cheaply.
public sealed class UnityCompileValidator : IValidator
{
    private readonly UnityChecksOptions _opts;

    public UnityCompileValidator(UnityChecksOptions? options = null)
    {
        _opts = options ?? new UnityChecksOptions();
    }

    public async Task<ValidationResult> ValidateAsync(string projectDir, CancellationToken ct = default)
    {
        var compile = await UnityChecks.CompileAsync(projectDir, _opts, ct);
        return new ValidationResult(
            Ok: compile.Ok,
            Summary: compile.Summary + (compile.LogPath is null ? "" : $" (log: {compile.LogPath})"),
            Errors: compile.Errors);
    }
}
