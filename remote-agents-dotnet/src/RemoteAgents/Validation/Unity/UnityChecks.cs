using RemoteAgents.Agents;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RemoteAgents.Primitives;

namespace RemoteAgents.Validation.Unity;

// Shared options. Defaults track Scaffold's validate-changes.ps1:
//   - Auto-discover the Unity install for the project (ProjectVersion.txt
//     → C:\Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe).
//   - Exclude perf/benchmark microbenchmarks by default (Card Framework
//     uses "Benchmark"; Scaffold uses "PerformanceBenchmark"). Override
//     via TestCategoryExclude.
//   - 10-minute compile / 30-minute test caps mirror Scaffold.
public sealed record UnityChecksOptions(
    string? UnityExePath = null,
    int CompileTimeoutMs = 10 * 60_000,
    int TestTimeoutMs = 30 * 60_000,
    int AnalyzerTimeoutMs = 10 * 60_000,
    string TestCategoryExclude = "Benchmark",
    // Restrict to specific assemblies (NUnit -assemblyNames arg). Null =
    // "all test assemblies in the project."
    IReadOnlyList<string>? AssemblyNames = null);

public sealed record UnityCheckResult(
    bool Ok,
    string Summary,
    string Errors,
    string? LogPath);

public sealed record UnityTestResult(
    bool Ok,
    int Total,
    int Passed,
    int Failed,
    int Skipped,
    IReadOnlyList<string> FailedTests,
    string Summary,
    string Errors,
    string? LogPath,
    string? ResultsXmlPath);

public sealed record UnityAnalyzerResult(
    bool Ok,
    int Total,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> Blockers,
    string Summary);

// The four Unity-validation primitives. Modeled on Scaffold's
// .agents/scripts/{check-unity-compilation,run-unity-tests,check-analyzers}.ps1:
//
//   CompileAsync           → `-batchmode -accept-apiupdate -projectPath ... -quit -logFile ...`
//                            (Unity launched, ~30s+)
//   EditModeTestsAsync     → `-batchmode -accept-apiupdate -projectPath ... -runTests
//                            -testPlatform EditMode -testCategory "!Benchmark"
//                            -testResults <xml> -logFile ...`
//                            (Unity launched, minutes)
//   PlayModeTestsAsync     → same as EditMode with -testPlatform PlayMode
//                            (Unity launched, minutes)
//   AnalyzersAsync         → `dotnet build <sln>` + parse `error|warning <CODE>:`
//                            (no Unity launch, seconds)
//
// Each returns a structured result so callers can format / commit / fix
// independently. Wrap them in UnityCompileValidator (fast preflight)
// or UnityFullValidator (Scaffold-style full gate) when an IValidator
// is needed.
public static class UnityChecks
{
    public static async Task<UnityCheckResult> CompileAsync(
        string projectDir,
        UnityChecksOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new UnityChecksOptions();
        var unityExe = ResolveUnityExe(projectDir, options.UnityExePath);
        var logFile = Path.Combine(Path.GetTempPath(), $"unity-compile-{Guid.NewGuid():N}.log");

        var args = new List<string>
        {
            "-batchmode", "-accept-apiupdate",
            "-projectPath", projectDir,
            "-quit",
            "-logFile", logFile,
        };

        var (exitCode, _) = await RunUnityAsync(unityExe, args, options.CompileTimeoutMs, ct);
        var log = File.Exists(logFile) ? await File.ReadAllTextAsync(logFile, ct) : "";

        // Scaffold's heuristic: success requires exit 0 AND no
        // `error CSxxxx` lines AND no project-lock detection.
        var compilerErrors = ExtractCompilerErrors(log);
        var projectLocked = log.Contains("another Unity instance is running with this project open", StringComparison.OrdinalIgnoreCase);

        if (exitCode == 0 && compilerErrors.Count == 0 && !projectLocked)
            return new UnityCheckResult(true, "compile OK", "", logFile);

        var summary = projectLocked
            ? $"compile blocked (project lock detected; another Unity instance has {projectDir} open)"
            : compilerErrors.Count > 0
                ? $"compile FAIL — {compilerErrors.Count} compiler errors"
                : $"compile FAIL — Unity exited {exitCode}";

        var errors = compilerErrors.Count > 0
            ? string.Join("\n", compilerErrors)
            : TailLines(log, 40);

        return new UnityCheckResult(false, summary, errors, logFile);
    }

    public static Task<UnityTestResult> EditModeTestsAsync(
        string projectDir,
        UnityChecksOptions? options = null,
        CancellationToken ct = default)
        => RunTestsAsync(projectDir, "EditMode", options ?? new UnityChecksOptions(), ct);

    public static Task<UnityTestResult> PlayModeTestsAsync(
        string projectDir,
        UnityChecksOptions? options = null,
        CancellationToken ct = default)
        => RunTestsAsync(projectDir, "PlayMode", options ?? new UnityChecksOptions(), ct);

    // dotnet build the auto-discovered .sln and pull analyzer-class
    // diagnostics out of the output. We treat anything matching
    //   error|warning <CODE>:
    // where CODE starts with a letter prefix (CS, RS, SCA, SCM, IDE, CA,
    // etc.) as a diagnostic. Build failures (BUILD_EXIT != 0 with no
    // parsed diagnostics) get reported as blockers.
    public static async Task<UnityAnalyzerResult> AnalyzersAsync(
        string projectDir,
        UnityChecksOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new UnityChecksOptions();

        var sln = Directory.EnumerateFiles(projectDir, "*.sln", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(projectDir, "*.slnx", SearchOption.TopDirectoryOnly))
            .FirstOrDefault();
        if (sln is null)
            return new UnityAnalyzerResult(
                Ok: true,
                Total: 0,
                Diagnostics: Array.Empty<string>(),
                Blockers: Array.Empty<string>(),
                Summary: $"analyzers skipped — no .sln/.slnx at {projectDir}");

        var res = await RunCommand.RunAsync(
            $"dotnet build \"{sln}\" --nologo -v quiet",
            new RunCommandOptions(Cwd: projectDir, TimeoutMs: options.AnalyzerTimeoutMs),
            ct);

        var diags = ExtractDiagnostics(res.Stdout + "\n" + res.Stderr);
        var blockers = new List<string>();
        if (res.TimedOut)
            blockers.Add($"dotnet build timed out after {options.AnalyzerTimeoutMs}ms");
        else if (res.ExitCode != 0 && diags.Count == 0)
            blockers.Add($"dotnet build exited {res.ExitCode} with no parseable diagnostics — likely a build configuration error.");

        var ok = diags.Count == 0 && blockers.Count == 0;
        var summary = ok
            ? "analyzers OK"
            : $"analyzers FAIL — {diags.Count} diagnostic(s), {blockers.Count} blocker(s)";

        return new UnityAnalyzerResult(ok, diags.Count, diags, blockers, summary);
    }

    private static async Task<UnityTestResult> RunTestsAsync(
        string projectDir, string testPlatform, UnityChecksOptions options, CancellationToken ct)
    {
        var unityExe = ResolveUnityExe(projectDir, options.UnityExePath);
        var laneTag = testPlatform.ToLowerInvariant();
        var temp = Path.Combine(Path.GetTempPath(), $"unity-{laneTag}-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        var logFile = Path.Combine(temp, $"{laneTag}.log");
        var resultsFile = Path.Combine(temp, $"{laneTag}-results.xml");

        var args = new List<string>
        {
            "-batchmode", "-accept-apiupdate",
            "-projectPath", projectDir,
            "-runTests",
            "-testPlatform", testPlatform,
            "-testResults", resultsFile,
            "-logFile", logFile,
        };

        if (!string.IsNullOrWhiteSpace(options.TestCategoryExclude))
        {
            args.Add("-testCategory");
            args.Add("!" + options.TestCategoryExclude);
        }
        if (options.AssemblyNames is { Count: > 0 })
        {
            args.Add("-assemblyNames");
            args.Add(string.Join(";", options.AssemblyNames));
        }

        var (exitCode, timedOut) = await RunUnityAsync(unityExe, args, options.TestTimeoutMs, ct);
        var log = File.Exists(logFile) ? await File.ReadAllTextAsync(logFile, ct) : "";

        if (timedOut)
        {
            return new UnityTestResult(
                Ok: false, Total: 0, Passed: 0, Failed: 0, Skipped: 0,
                FailedTests: Array.Empty<string>(),
                Summary: $"{testPlatform} BLOCKED — Unity timed out after {options.TestTimeoutMs}ms",
                Errors: TailLines(log, 40),
                LogPath: logFile, ResultsXmlPath: null);
        }
        if (!File.Exists(resultsFile))
        {
            return new UnityTestResult(
                Ok: false, Total: 0, Passed: 0, Failed: 0, Skipped: 0,
                FailedTests: Array.Empty<string>(),
                Summary: $"{testPlatform} BLOCKED — Unity exited {exitCode} without writing results XML",
                Errors: TailLines(log, 40),
                LogPath: logFile, ResultsXmlPath: null);
        }

        var parsed = ParseNUnitResults(resultsFile);
        var ok = parsed.Failed == 0 && exitCode == 0;
        var summary = ok
            ? $"{testPlatform} OK — {parsed.Total} run, {parsed.Passed} passed, {parsed.Skipped} skipped"
            : $"{testPlatform} FAIL — {parsed.Failed} failed of {parsed.Total} ({parsed.Passed} passed, {parsed.Skipped} skipped)";

        return new UnityTestResult(
            Ok: ok,
            Total: parsed.Total, Passed: parsed.Passed, Failed: parsed.Failed, Skipped: parsed.Skipped,
            FailedTests: parsed.FailedTests,
            Summary: summary,
            Errors: parsed.Failed == 0 ? "" : string.Join("\n", parsed.FailedTests),
            LogPath: logFile,
            ResultsXmlPath: resultsFile);
    }

    // Direct Process.Start with ArgumentList — avoids cmd.exe quoting
    // around the Unity Hub install path (which has a space).
    private static async Task<(int ExitCode, bool TimedOut)> RunUnityAsync(
        string unityExe, IReadOnlyList<string> args, int timeoutMs, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = unityExe,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.Start();

        using var timeoutCts = new CancellationTokenSource(timeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var timedOut = false;
        try { await proc.WaitForExitAsync(linkedCts.Token); }
        catch (OperationCanceledException)
        {
            timedOut = timeoutCts.IsCancellationRequested;
            try { proc.Kill(entireProcessTree: true); } catch { }
            try { await proc.WaitForExitAsync(CancellationToken.None); } catch { }
            if (!timedOut) throw;
        }

        return (proc.HasExited ? proc.ExitCode : -1, timedOut);
    }

    private static string ResolveUnityExe(string projectDir, string? explicitPath)
    {
        if (explicitPath is not null && File.Exists(explicitPath)) return explicitPath;
        return FindUnityForProject(projectDir)
            ?? throw new FileNotFoundException(
                $"Could not locate a Unity install matching {projectDir}'s ProjectVersion.txt. " +
                "Install the required version via Unity Hub, or pass UnityChecksOptions.UnityExePath.");
    }

    // Public for callers that want to surface the resolved path in
    // their banner (the smoke flow does).
    public static string? FindUnityForProject(string projectDir)
    {
        var projectVersionFile = Path.Combine(projectDir, "ProjectSettings", "ProjectVersion.txt");
        if (!File.Exists(projectVersionFile)) return null;
        var line = File.ReadLines(projectVersionFile)
            .FirstOrDefault(l => l.StartsWith("m_EditorVersion:", StringComparison.Ordinal));
        if (line is null) return null;
        var version = line["m_EditorVersion:".Length..].Trim();
        var exe = Path.Combine(@"C:\Program Files\Unity\Hub\Editor", version, "Editor", "Unity.exe");
        return File.Exists(exe) ? exe : null;
    }

    private static readonly Regex CompilerError = new(@"\berror CS\d+:", RegexOptions.Compiled);

    private static List<string> ExtractCompilerErrors(string log)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hits = new List<string>();
        foreach (var raw in log.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0) continue;
            if (CompilerError.IsMatch(line) && seen.Add(line)) hits.Add(line);
        }
        return hits;
    }

    // Match `<path>(line,col): error|warning <CODE>: <msg>` where CODE
    // is a letter-prefixed identifier (CS, RS, SCA, SCM, IDE, CA, ...).
    // Scoped wider than just SCA so we surface Roslyn analyzers from
    // any pack the project pulls in.
    private static readonly Regex DiagnosticLine = new(
        @"^\s*.+?\(\d+,\d+\):\s+(error|warning)\s+[A-Z]{2,5}\d+\s*:\s+.+$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static IReadOnlyList<string> ExtractDiagnostics(string buildOutput)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hits = new List<string>();
        foreach (Match m in DiagnosticLine.Matches(buildOutput))
        {
            var line = m.Value.Trim();
            if (seen.Add(line)) hits.Add(line);
        }
        return hits;
    }

    private static string TailLines(string text, int n)
    {
        var lines = text.Split('\n');
        return lines.Length <= n ? text : string.Join('\n', lines[^n..]);
    }

    public sealed record NUnitParsed(
        int Total, int Passed, int Failed, int Skipped, IReadOnlyList<string> FailedTests);

    // Parse Unity Test Framework's NUnit3-shaped XML. Unity emits a
    // <test-run> root with total/passed/failed/skipped attributes; we
    // also walk <test-case result="Failed"> for the names so the fix
    // prompt can name them.
    public static NUnitParsed ParseNUnitResults(string path)
    {
        var doc = XDocument.Load(path);
        var run = doc.Root;
        if (run is null || run.Name.LocalName != "test-run")
            return new NUnitParsed(0, 0, 0, 0, Array.Empty<string>());

        int Int(string attr) => int.TryParse((string?)run.Attribute(attr), out var n) ? n : 0;

        var failed = new List<string>();
        foreach (var tc in run.Descendants("test-case"))
        {
            var result = (string?)tc.Attribute("result");
            if (string.Equals(result, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                var name = (string?)tc.Attribute("fullname") ?? (string?)tc.Attribute("name") ?? "<unnamed>";
                var msg = tc.Element("failure")?.Element("message")?.Value?.Trim();
                failed.Add(string.IsNullOrEmpty(msg) ? name : $"{name}\n  {Indent(msg)}");
            }
        }

        return new NUnitParsed(
            Total: Int("total"),
            Passed: Int("passed"),
            Failed: Int("failed"),
            Skipped: Int("skipped"),
            FailedTests: failed);
    }

    private static string Indent(string msg) =>
        string.Join("\n  ", msg.Split('\n').Select(l => l.TrimEnd('\r')));
}
