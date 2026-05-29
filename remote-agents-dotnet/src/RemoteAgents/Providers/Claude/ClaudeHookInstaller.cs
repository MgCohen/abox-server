namespace RemoteAgents.Agents;

// IHookInstaller<ClaudeAgent> wrapper around the static ClaudeHookConfig.
// Singleton in DI; the IAsyncDisposable scope returned from InstallAsync
// owns the matching Uninstall call.
public sealed class ClaudeHookInstaller : IHookInstaller<ClaudeAgent>
{
    public Task<IAsyncDisposable> InstallAsync(AgentRunRequest req, string shimPath, CancellationToken ct)
    {
        ClaudeHookConfig.Install(req.ProjectDir, shimPath);
        return Task.FromResult<IAsyncDisposable>(new Scope(req.ProjectDir));
    }

    private sealed class Scope(string projectDir) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            ClaudeHookConfig.Uninstall(projectDir);
            return ValueTask.CompletedTask;
        }
    }
}
