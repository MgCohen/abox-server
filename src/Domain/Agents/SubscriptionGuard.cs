namespace RemoteAgents.Infrastructure.CommandLine;

public static class SubscriptionGuard
{
    public static async Task CheckAsync(IReadOnlyList<string> forbiddenKeys, string binary, CancellationToken ct = default)
    {
        var present = forbiddenKeys
            .Where(k => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(k)))
            .ToList();

        if (present.Count > 0)
            throw new InvalidOperationException(
                $"Refusing to start: {string.Join(", ", present)} is set in the environment.\n" +
                $"With these set, `{binary}` bills against the API instead of the subscription,\n" +
                "defeating the point of this orchestrator. Unset and re-run.");

        var version = await RunCommand.RunAsync($"{binary} --version", new RunCommandOptions(TimeoutMs: 10_000), ct);
        if (version.ExitCode != 0)
            throw new InvalidOperationException(
                $"Refusing to start: `{binary} --version` returned exit {version.ExitCode}.\n" +
                $"The `{binary}` CLI must be on PATH for this orchestrator to work.\n" +
                $"stdout: {version.Stdout.Trim()}\nstderr: {version.Stderr.Trim()}");
    }
}
