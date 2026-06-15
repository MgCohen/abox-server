namespace ABox.Infrastructure.Storage;

public sealed record StorageRoot(string Folder)
{
    // rebuild/ isolates from the quarantined prototype's data; reverts at L12. Mirrors FileFlowHistory.
    public static StorageRoot Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".abox", "rebuild"));
}
