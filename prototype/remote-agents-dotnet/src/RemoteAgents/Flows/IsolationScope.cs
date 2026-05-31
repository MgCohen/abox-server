using RemoteAgents.Primitives;

namespace RemoteAgents.Flows;

// Captures the set of files an agent has touched at Begin(), then on
// DisposeAsync reverts every other unstaged change. Used by validators
// that dirty the tree on every run (Unity batch-mode regenerates
// TMP_SDF / .meta / Library, etc.) — the agent's real changes stay,
// the validator's noise gets cleaned up.
public sealed class IsolationScope : IAsyncDisposable
{
    private readonly string _projectDir;
    private readonly IReadOnlyList<string> _agentTouched;

    private IsolationScope(string projectDir, IReadOnlyList<string> agentTouched)
    {
        _projectDir = projectDir;
        _agentTouched = agentTouched;
    }

    public static async Task<IsolationScope> BeginAsync(string projectDir, CancellationToken ct = default)
    {
        var touched = await GitOps.ChangedFilesAsync(projectDir, ct);
        return new IsolationScope(projectDir, touched);
    }

    public async ValueTask DisposeAsync()
    {
        await GitOps.RestoreUnstagedExceptAsync(_projectDir, _agentTouched, CancellationToken.None);
    }
}
