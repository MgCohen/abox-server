namespace RemoteAgents.Agents;

// IHookInstaller<CodexAgent> wrapper around the static CodexHookConfig.
// Codex hooks are user-global (~/.codex/hooks.json) — ignores
// req.ProjectDir and resolves the config dir itself.
public sealed class CodexHookInstaller : IHookInstaller<CodexAgent>
{
    public Task<IAsyncDisposable> InstallAsync(AgentRunRequest req, string shimPath, CancellationToken ct)
    {
        var configDir = CodexHookConfig.DefaultConfigDir();
        CodexHookConfig.Install(configDir, shimPath);
        return Task.FromResult<IAsyncDisposable>(new Scope(configDir));
    }

    private sealed class Scope(string configDir) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            CodexHookConfig.Uninstall(configDir);
            return ValueTask.CompletedTask;
        }
    }
}
