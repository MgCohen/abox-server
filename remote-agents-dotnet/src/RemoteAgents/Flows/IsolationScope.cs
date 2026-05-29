using RemoteAgents.Events;
using RemoteAgents.Primitives;

namespace RemoteAgents.Flows;

// Captures the set of files an agent has touched at Begin(), then on
// DisposeAsync reverts every other unstaged change. Used by validators
// that dirty the tree on every run (Unity batch-mode regenerates
// TMP_SDF / .meta / Library, etc.) — the agent's real changes stay,
// the validator's noise gets cleaned up.
//
// Typical usage in a flow:
//     await using var iso = await IsolationScope.BeginAsync(projectDir);
//     var result = await Loops.ValidateAndFixAsync(...);
//     // on scope exit, validator-generated files are reverted
public sealed class IsolationScope : IAsyncDisposable
{
    private readonly string _projectDir;
    private readonly IReadOnlyList<string> _agentTouched;
    private readonly IEventSink _sink;

    private IsolationScope(string projectDir, IReadOnlyList<string> agentTouched, IEventSink sink)
    {
        _projectDir = projectDir;
        _agentTouched = agentTouched;
        _sink = sink;
    }

    public static async Task<IsolationScope> BeginAsync(
        string projectDir,
        IEventSink? sink = null,
        CancellationToken ct = default)
    {
        var touched = await GitOps.ChangedFilesAsync(projectDir, ct);
        return new IsolationScope(projectDir, touched, sink ?? NoOpSink.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        var reverted = await GitOps.RestoreUnstagedExceptAsync(_projectDir, _agentTouched, CancellationToken.None);
        if (reverted.Count > 0)
            await _sink.PhaseInfoAsync("cleanup", $"reverted {reverted.Count} validator-generated files.", CancellationToken.None);
    }
}
