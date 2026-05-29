using RemoteAgents.Primitives;

namespace RemoteAgents.Hosting;

// Reads prompts/<name>.md from disk on every Load() call, so editing the
// markdown picks up on the next agent run — no rebuild. Resolved by
// walking up from CWD (and the executing assembly's BaseDirectory) until
// we find a parent containing the `remote-agents-dotnet/` directory; same
// pattern Session uses to locate sessions/.
public static class Prompts
{
    public static string Load(string name)
    {
        var path = ResolvePath(name)
            ?? throw new FileNotFoundException(
                $"Prompt not found: {name}.md. Looked for `<repo>/remote-agents-dotnet/src/RemoteAgents.Hosting/prompts/{name}.md` " +
                $"starting from CWD={Environment.CurrentDirectory} and BaseDirectory={AppContext.BaseDirectory}.");

        return File.ReadAllText(path);
    }

    // Public so callers can warn at startup if a prompt is missing.
    public static string? ResolvePath(string name)
    {
        var repoRoot = RepoRoot.Find("remote-agents-dotnet");
        if (repoRoot is null) return null;
        var path = Path.Combine(repoRoot, "remote-agents-dotnet", "src", "RemoteAgents.Hosting", "prompts", $"{name}.md");
        return File.Exists(path) ? path : null;
    }
}
