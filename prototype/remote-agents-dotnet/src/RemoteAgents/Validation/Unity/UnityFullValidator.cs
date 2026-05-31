using RemoteAgents.Agents;
using System.Text;
using RemoteAgents.Validation;

namespace RemoteAgents.Validation.Unity;

public sealed record UnityFullValidatorOptions(
    UnityChecksOptions Checks,
    // Skip the compile-only preflight (steps 4/5 still run a compile
    // implicitly). Off by default — Scaffold runs the preflight to
    // fail fast on broken builds before paying for a full test run.
    bool SkipCompilePreflight = false,
    bool SkipEditModeTests = false,
    bool SkipPlayModeTests = false,
    bool SkipAnalyzers = false);

// Card Framework / Scaffold-canonical pipeline. Runs UnityChecks in
// Scaffold's order:
//
//   1. Compile preflight (Unity -quit). Short-circuits the rest if it
//      fails so we don't waste 30 min on a broken build.
//   2. EditMode tests (Unity -runTests EditMode).
//   3. PlayMode tests (Unity -runTests PlayMode).
//   4. Analyzers (dotnet build + parse). Runs even when tests fail,
//      because analyzer diagnostics are independently actionable.
//
// Returns one ValidationResult with a multi-section Errors string so
// the fix prompt can address all failing categories at once.
public sealed class UnityFullValidator : IValidator
{
    private readonly UnityFullValidatorOptions _opts;

    public UnityFullValidator(UnityFullValidatorOptions? options = null)
    {
        _opts = options ?? new UnityFullValidatorOptions(Checks: new UnityChecksOptions());
    }

    public async Task<ValidationResult> ValidateAsync(string projectDir, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        var sections = new List<string>();
        var passed = new List<string>();
        var allOk = true;

        if (!_opts.SkipCompilePreflight)
        {
            var compile = await UnityChecks.CompileAsync(projectDir, _opts.Checks, ct);
            if (!compile.Ok)
            {
                // Compile failure short-circuits — the test runs would
                // just re-report the same errors after another 30s of
                // Unity startup. Analyzers also need a buildable sln.
                return new ValidationResult(
                    Ok: false,
                    Summary: $"unity full: {compile.Summary} (skipped EditMode/PlayMode/analyzers — fix compile first)",
                    Errors: compile.Errors);
            }
            passed.Add("compile");
        }

        UnityTestResult? edit = null, play = null;

        if (!_opts.SkipEditModeTests)
        {
            edit = await UnityChecks.EditModeTestsAsync(projectDir, _opts.Checks, ct);
            if (!edit.Ok)
            {
                allOk = false;
                sb.AppendLine("### EditMode tests");
                sb.AppendLine(edit.Errors);
                sb.AppendLine();
                sections.Add(edit.Summary);
            }
            else passed.Add("EditMode");
        }

        if (!_opts.SkipPlayModeTests)
        {
            play = await UnityChecks.PlayModeTestsAsync(projectDir, _opts.Checks, ct);
            if (!play.Ok)
            {
                allOk = false;
                sb.AppendLine("### PlayMode tests");
                sb.AppendLine(play.Errors);
                sb.AppendLine();
                sections.Add(play.Summary);
            }
            else passed.Add("PlayMode");
        }

        if (!_opts.SkipAnalyzers)
        {
            var analyzers = await UnityChecks.AnalyzersAsync(projectDir, _opts.Checks, ct);
            if (!analyzers.Ok)
            {
                allOk = false;
                sb.AppendLine("### Analyzer diagnostics");
                foreach (var b in analyzers.Blockers) sb.AppendLine("BLOCKER: " + b);
                foreach (var d in analyzers.Diagnostics) sb.AppendLine(d);
                sb.AppendLine();
                sections.Add(analyzers.Summary);
            }
            else passed.Add("analyzers");
        }

        var summary = allOk
            ? $"unity full OK — {string.Join(", ", passed)} passed"
            : $"unity full FAIL — {string.Join("; ", sections)}";
        return new ValidationResult(Ok: allOk, Summary: summary, Errors: sb.ToString().TrimEnd());
    }
}
