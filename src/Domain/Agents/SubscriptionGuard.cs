namespace ABox.Domain.Agents;

internal static class SubscriptionGuard
{
    // Billing safety only: refuse to start if an API key is set, since `binary` would
    // then bill the API instead of the subscription. No CLI-on-PATH probe — claude runs
    // in the box now, not on the host (ADR 0013), so the host needs docker, not claude.
    public static Task CheckAsync(IReadOnlyList<string> forbiddenKeys, string binary, CancellationToken ct = default)
    {
        var present = forbiddenKeys
            .Where(k => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(k)))
            .ToList();

        if (present.Count > 0)
            throw new InvalidOperationException(
                $"Refusing to start: {string.Join(", ", present)} is set in the environment.\n" +
                $"With these set, `{binary}` bills against the API instead of the subscription,\n" +
                "defeating the point of this orchestrator. Unset and re-run.");

        return Task.CompletedTask;
    }
}
