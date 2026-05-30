using RemoteAgents.Agents;
using System.Text;
using System.Text.RegularExpressions;
using RemoteAgents.Primitives;
using RemoteAgents.Validation;

namespace RemoteAgents.Validation.Dotnet;

public sealed record DotnetValidatorOptions(
    // Path (relative to projectDir, or absolute) to a .sln/.slnx/.csproj
    // to build. Null = "auto-discover one in projectDir." When more than
    // one match exists, validation fails with a clear summary instead of
    // picking randomly.
    string? Target = null,
    // Run `dotnet test` after a clean build. Off by default — the
    // OrchestratorValidator catches parse errors cheaply; tests get
    // turned on by flows that own a test-clean baseline.
    bool RunTests = false,
    // Per-step timeout. dotnet test on a cold cache can take minutes.
    int BuildTimeoutMs = 5 * 60_000,
    int TestTimeoutMs = 10 * 60_000,
    string Configuration = "Debug");

// Run `dotnet build` (+ optional `dotnet test`) and convert the output
// into a ValidationResult. The existing OrchestratorValidator only
// checks syntax via Roslyn — it can't catch name resolution, missing
// references, type errors, or test regressions. This one can.
//
// Output parsing: dotnet build emits errors in a stable format —
// `<path>(line,col): error CSxxxx: <msg>` — easy to grep for. We also
// surface the "Build succeeded / Build FAILED" line directly.
public sealed class DotnetValidator : IValidator
{
    private readonly DotnetValidatorOptions _opts;

    public DotnetValidator(DotnetValidatorOptions? options = null)
    {
        _opts = options ?? new DotnetValidatorOptions();
    }

    public async Task<ValidationResult> ValidateAsync(string projectDir, CancellationToken ct = default)
    {
        var target = ResolveTarget(projectDir, _opts.Target);
        if (target is { Found: false })
        {
            return new ValidationResult(
                Ok: false,
                Summary: target.Error,
                Errors: "");
        }

        var targetArg = target!.Path is null ? "" : " " + Shell.QuoteArg(target.Path);

        var build = await RunCommand.RunAsync(
            $"dotnet build{targetArg} --nologo --configuration {_opts.Configuration}",
            new RunCommandOptions(Cwd: projectDir, TimeoutMs: _opts.BuildTimeoutMs), ct);

        if (build.TimedOut)
            return new ValidationResult(false, $"dotnet build timed out after {_opts.BuildTimeoutMs}ms", build.Stdout + build.Stderr);

        if (build.ExitCode != 0)
        {
            var errors = ExtractBuildErrors(build.Stdout, build.Stderr);
            return new ValidationResult(
                Ok: false,
                Summary: $"dotnet build FAILED (exit {build.ExitCode}){target.SummaryTail}",
                Errors: errors);
        }

        if (!_opts.RunTests)
            return new ValidationResult(
                Ok: true,
                Summary: $"dotnet build OK{target.SummaryTail}",
                Errors: "");

        var test = await RunCommand.RunAsync(
            $"dotnet test{targetArg} --nologo --no-build --configuration {_opts.Configuration}",
            new RunCommandOptions(Cwd: projectDir, TimeoutMs: _opts.TestTimeoutMs), ct);

        if (test.TimedOut)
            return new ValidationResult(false, $"dotnet test timed out after {_opts.TestTimeoutMs}ms", test.Stdout + test.Stderr);

        if (test.ExitCode != 0)
        {
            var errors = ExtractTestErrors(test.Stdout, test.Stderr);
            return new ValidationResult(
                Ok: false,
                Summary: $"dotnet test FAILED (exit {test.ExitCode}){target.SummaryTail}",
                Errors: errors);
        }

        return new ValidationResult(
            Ok: true,
            Summary: $"dotnet build + test OK{target.SummaryTail}",
            Errors: "");
    }

    private sealed record TargetResolution(bool Found, string? Path, string Error, string SummaryTail);

    private static TargetResolution ResolveTarget(string projectDir, string? explicitTarget)
    {
        if (explicitTarget is not null)
        {
            var abs = Path.IsPathRooted(explicitTarget) ? explicitTarget : Path.Combine(projectDir, explicitTarget);
            if (!File.Exists(abs))
                return new TargetResolution(false, null, $"dotnet target not found: {abs}", "");
            return new TargetResolution(true, abs, "", $" ({Path.GetFileName(abs)})");
        }

        // Auto-discover. Solutions beat project files; .slnx is preferred
        // over legacy .sln (the orchestrator already uses .slnx).
        string[] patterns = ["*.slnx", "*.sln", "*.csproj"];
        foreach (var pattern in patterns)
        {
            var hits = Directory.GetFiles(projectDir, pattern, SearchOption.TopDirectoryOnly);
            if (hits.Length == 1)
                return new TargetResolution(true, hits[0], "", $" ({Path.GetFileName(hits[0])})");
            if (hits.Length > 1)
                return new TargetResolution(false, null,
                    $"dotnet target ambiguous: {hits.Length} {pattern} files in {projectDir}; pass Target explicitly", "");
        }

        // No explicit target, none found — let `dotnet build` figure it
        // out (it walks for a .csproj). The summary won't pin a name.
        return new TargetResolution(true, null, "", "");
    }

    private static readonly Regex BuildErrorLine = new(
        @"^.*?\): (?:error|fatal)[^:]*:.*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static string ExtractBuildErrors(string stdout, string stderr)
    {
        var sb = new StringBuilder();
        foreach (var line in BuildErrorLine.Matches(stdout).Cast<Match>())
            sb.AppendLine(line.Value.Trim());
        // dotnet build sometimes writes the failing target to stderr;
        // fall back to that if we found nothing parseable.
        if (sb.Length == 0)
            sb.Append(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
        return sb.ToString().TrimEnd();
    }

    private static readonly Regex TestFailLine = new(
        @"^\s*(Failed|X)\s+.+$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static string ExtractTestErrors(string stdout, string stderr)
    {
        var sb = new StringBuilder();
        foreach (var line in TestFailLine.Matches(stdout).Cast<Match>())
            sb.AppendLine(line.Value.TrimEnd());
        // The final "Failed!  - Failed:N..." summary line is the most
        // informative single line; include it verbatim if present.
        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("Failed!") || line.StartsWith("Passed!"))
                sb.AppendLine(line);
        }
        if (sb.Length == 0)
            sb.Append(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
        return sb.ToString().TrimEnd();
    }

}
