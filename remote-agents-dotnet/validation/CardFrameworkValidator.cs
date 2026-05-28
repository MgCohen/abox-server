using RemoteAgents.Primitives;

namespace RemoteAgents.Validation.CardFramework;

// Runs Unity in batch mode against a Card Framework checkout and returns
// pass/fail based on the editor compile result. Used by the step-15
// shakedown flow.
//
// Unity batch-mode invocation reference:
//   Unity.exe -batchmode -nographics -quit
//             -projectPath <projectDir>
//             -logFile <logFile>
//
// Exit codes (per Unity docs):
//   0  — success
//   1  — Unity threw / failed
//   2  — assembly compile errors
//
// We treat any non-zero as a validation failure and surface the tail of
// the editor log so the orchestrator can feed it back to Claude.
public sealed class CardFrameworkValidator : IValidator
{
    public string UnityExePath { get; init; }
    public int TimeoutMs { get; init; } = 10 * 60_000;

    // Discovered automatically from C:\Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe
    // if not set explicitly. Latest version wins.
    public CardFrameworkValidator(string? unityExePath = null)
    {
        UnityExePath = unityExePath ?? FindUnity()
            ?? throw new FileNotFoundException(
                "Unity not found. Pass an absolute path to Unity.exe, or install via Unity Hub.");
    }

    public async Task<ValidationResult> ValidateAsync(string projectDir, CancellationToken ct = default)
    {
        var logFile = Path.Combine(Path.GetTempPath(), $"unity-batch-{Guid.NewGuid():N}.log");

        // Quoting handled by the cmd.exe shell that RunCommand wraps around.
        var cmd = $"\"{UnityExePath}\" -batchmode -nographics -quit " +
                  $"-projectPath \"{projectDir}\" -logFile \"{logFile}\"";

        var res = await RunCommand.RunAsync(cmd,
            new RunCommandOptions(TimeoutMs: TimeoutMs), ct);

        var log = File.Exists(logFile) ? await File.ReadAllTextAsync(logFile, ct) : "";

        if (res.ExitCode == 0)
        {
            return new ValidationResult(
                Ok: true,
                Summary: $"OK — Unity batch-mode compile clean ({logFile})",
                Errors: "");
        }

        // Tail of the editor log is usually where compile errors land.
        var tail = TailLines(log, 100);
        return new ValidationResult(
            Ok: false,
            Summary: $"FAIL — Unity exited {res.ExitCode} (log: {logFile})",
            Errors: tail);
    }

    private static string TailLines(string text, int n)
    {
        var lines = text.Split('\n');
        return lines.Length <= n ? text : string.Join('\n', lines[^n..]);
    }

    private static string? FindUnity()
    {
        var hubRoot = @"C:\Program Files\Unity\Hub\Editor";
        if (!Directory.Exists(hubRoot)) return null;
        var versions = Directory.EnumerateDirectories(hubRoot)
            .Select(p => (path: p, name: Path.GetFileName(p)))
            .OrderByDescending(v => v.name, StringComparer.Ordinal)  // latest by lexicographic version
            .ToList();
        foreach (var v in versions)
        {
            var exe = Path.Combine(v.path, "Editor", "Unity.exe");
            if (File.Exists(exe)) return exe;
        }
        return null;
    }
}
