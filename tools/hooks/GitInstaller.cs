namespace ABox.Governance.Hooks;

public static class GitInstaller
{
    public const string Marker = "abox-hooks commit";

    private const string Script = "#!/bin/sh\nexec abox-hooks commit\n";

    public static InstallResult InstallPostCommit(string repoDir)
    {
        var gitDir = Git.Output(repoDir, "rev-parse", "--git-dir");
        if (gitDir is null) return new InstallResult(false, $"not a git repo: {repoDir}");

        var hooksPath = Git.Output(repoDir, "config", "--get", "core.hooksPath");
        if (!string.IsNullOrEmpty(hooksPath))
            return new InstallResult(false,
                $"core.hooksPath is set to '{hooksPath}' — refusing to touch a managed hooks dir. " +
                $"Add `{Marker}` to that dir's post-commit by hand.");

        var hooksDir = Path.IsPathRooted(gitDir) ? Path.Combine(gitDir, "hooks") : Path.Combine(repoDir, gitDir, "hooks");
        Directory.CreateDirectory(hooksDir);

        var postCommit = Path.Combine(hooksDir, "post-commit");
        if (File.Exists(postCommit) && !File.ReadAllText(postCommit).Contains(Marker))
            return new InstallResult(false, $"a post-commit hook already exists at {postCommit} — leaving it untouched.");

        File.WriteAllText(postCommit, Script);
        MakeExecutable(postCommit);
        return new InstallResult(true, $"installed post-commit → `{Marker}` at {postCommit}");
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
}
