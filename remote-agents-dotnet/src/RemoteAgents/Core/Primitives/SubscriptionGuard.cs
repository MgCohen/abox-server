namespace RemoteAgents.Primitives;

// Refuse to start a flow if API-key env vars are set (would defeat
// subscription billing) or if claude/codex CLIs aren't on PATH. Q17.
public static class SubscriptionGuard
{
    private static readonly string[] ForbiddenEnvVars =
    [
        "ANTHROPIC_API_KEY",
        "CLAUDE_API_KEY",
        "OPENAI_API_KEY",
    ];

    public static async Task CheckAsync(CancellationToken ct = default)
    {
        var bad = new List<string>();
        foreach (var v in ForbiddenEnvVars)
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(v))) bad.Add(v);
        }
        if (bad.Count > 0)
        {
            throw new InvalidOperationException(
                $"Refusing to start: {string.Join(", ", bad)} is set in the environment.\n" +
                "If set, the claude/codex CLIs bill against the API instead of the\n" +
                "Max / ChatGPT subscription, defeating the point of this orchestrator.\n" +
                "Unset and re-run.");
        }

        await EnsureBinaryAsync("claude", ct);
        await EnsureBinaryAsync("codex", ct);
    }

    private static async Task EnsureBinaryAsync(string name, CancellationToken ct)
    {
        var res = await RunCommand.RunAsync($"{name} --version", new RunCommandOptions(TimeoutMs: 10_000), ct);
        if (res.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Refusing to start: `{name} --version` returned exit {res.ExitCode}.\n" +
                $"The `{name}` CLI must be on PATH for this orchestrator to work.\n" +
                $"stdout: {res.Stdout.Trim()}\nstderr: {res.Stderr.Trim()}");
        }
    }
}
