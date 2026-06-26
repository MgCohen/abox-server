namespace ABox.Domain.Git;

public sealed record CommitResult(string Hash, string Subject)
{
    public override string ToString() => $"committed {Short(Hash)}: {Subject}";

    private static string Short(string hash) => hash.Length >= 7 ? hash[..7] : hash;
}
