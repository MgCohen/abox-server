namespace ABox.Domain.Git;

public sealed record StatusResult(bool IsDirty, IReadOnlyList<string> Paths)
{
    public override string ToString() =>
        IsDirty ? $"working tree dirty: {Paths.Count} file(s)" : "working tree clean";
}
