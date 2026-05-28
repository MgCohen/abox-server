using System.Diagnostics;

namespace RemoteAgents.Validation.Unity;

// Runs Unity in batch mode against any Unity project and returns pass/fail
// based on the editor compile result. Used by the step-15 shakedown flow
// and any future per-project validator that needs a "did Unity compile?"
// check.
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
public sealed class UnityBatchValidator : IValidator
{
    // null = pick automatically per project by reading ProjectVersion.txt
    // and matching against C:\Program Files\Unity\Hub\Editor\<version>\.
    public string? UnityExePath { get; init; }
    public int TimeoutMs { get; init; } = 10 * 60_000;

    public UnityBatchValidator(string? unityExePath = null)
    {
        UnityExePath = unityExePath;
    }

    public async Task<ValidationResult> ValidateAsync(string projectDir, CancellationToken ct = default)
    {
        var unityExe = UnityExePath ?? FindUnityForProject(projectDir)
            ?? throw new FileNotFoundException(
                $"Could not locate a Unity install matching {projectDir}'s ProjectVersion.txt. " +
                "Install the required version via Unity Hub, or pass an explicit UnityExePath.");

        var logFile = Path.Combine(Path.GetTempPath(), $"unity-batch-{Guid.NewGuid():N}.log");

        // Direct Process.Start with ArgumentList — avoids cmd.exe's quoting
        // rules, which strip outer quotes around paths with spaces (the
        // Unity Hub install path has one).
        var psi = new ProcessStartInfo
        {
            FileName = unityExe,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-batchmode");
        psi.ArgumentList.Add("-nographics");
        psi.ArgumentList.Add("-quit");
        psi.ArgumentList.Add("-projectPath");
        psi.ArgumentList.Add(projectDir);
        psi.ArgumentList.Add("-logFile");
        psi.ArgumentList.Add(logFile);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.Start();

        using var timeoutCts = new CancellationTokenSource(TimeoutMs);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try { await proc.WaitForExitAsync(linkedCts.Token); }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            try { await proc.WaitForExitAsync(CancellationToken.None); } catch { }
            if (!timeoutCts.IsCancellationRequested) throw;
        }

        var log = File.Exists(logFile) ? await File.ReadAllTextAsync(logFile, ct) : "";
        var exitCode = proc.HasExited ? proc.ExitCode : -1;

        if (exitCode == 0)
        {
            return new ValidationResult(
                Ok: true,
                Summary: $"OK — Unity batch-mode compile clean ({logFile})",
                Errors: "");
        }

        var tail = TailLines(log, 100);
        return new ValidationResult(
            Ok: false,
            Summary: $"FAIL — Unity exited {exitCode} (log: {logFile})",
            Errors: tail);
    }

    private static string TailLines(string text, int n)
    {
        var lines = text.Split('\n');
        return lines.Length <= n ? text : string.Join('\n', lines[^n..]);
    }

    private static string? FindUnityForProject(string projectDir)
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
}
