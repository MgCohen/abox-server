using System.Diagnostics;

namespace ABox.Governance.Hooks;

public static class Git
{
    public static string? Output(string repoDir, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = repoDir,
            };
            psi.ArgumentList.Add("-C");
            psi.ArgumentList.Add(repoDir);
            foreach (var a in args) psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            return proc.ExitCode == 0 ? stdout.Trim() : null;
        }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
