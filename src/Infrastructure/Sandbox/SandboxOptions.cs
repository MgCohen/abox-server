namespace ABox.Infrastructure.Sandbox;

public sealed record SandboxOptions(
    DirectoryInfo Worktree,
    DirectoryInfo SessionDir,
    DirectoryInfo Home,
    string Image,
    string? Network = null,
    bool RequireInternalNetwork = false);
