namespace ABox.Domain.Agents.Codex;

// The credential template for codex boxes: a temp dir holding `.codex/auth.json` copied from
// the host's ChatGPT-login auth (subscription tokens, ADR 0013). Built once at composition;
// CodexProvider copies it into a per-turn-session box HOME so each provider gets its own
// writable .codex (sessions, refresh) without sharing state. Only auth.json is carried — never
// the host's full ~/.codex (sessions, memories, sqlite). No copy happens if the host is
// unprovisioned; the turn then fails auth rather than mis-billing.
public static class CodexHome
{
    public static DirectoryInfo MaterializeTemplate()
    {
        var dir = Directory.CreateTempSubdirectory("abox-codex-auth-");
        var codexDir = Directory.CreateDirectory(Path.Combine(dir.FullName, ".codex"));

        var hostAuth = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "auth.json");
        if (File.Exists(hostAuth))
        {
            var dest = Path.Combine(codexDir.FullName, "auth.json");
            File.Copy(hostAuth, dest, overwrite: true);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(dest, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        return dir;
    }
}
