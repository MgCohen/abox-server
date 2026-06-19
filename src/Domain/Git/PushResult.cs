namespace ABox.Domain.Git;

public sealed record PushResult(string Remote, string Branch)
{
    public override string ToString() => $"pushed {Branch} → {Remote}";
}
