namespace ABox.Infrastructure.Sandbox;

public sealed record SandboxOptions(
    DirectoryInfo Worktree,
    DirectoryInfo SessionDir,
    string Image,
    string? Network = null);
