namespace Probe.Domain;

// The aggregate the recipe mutates and the command that triggers it. Reused infra.
// `User` is loaded from whichever provider is plugged, mutated, and saved back — the
// SAME output regardless of provider. The command carries both a repo-style key
// (Email) and a bucket-style key (Region) so each provider can select its own.

public sealed class User(string id)
{
    public string Id { get; } = id;
    public int Points { get; private set; }
    public void AddPoints(int n) => Points += n;
}

public sealed record AddPointsCommand(string Email, string Region, int Points);
