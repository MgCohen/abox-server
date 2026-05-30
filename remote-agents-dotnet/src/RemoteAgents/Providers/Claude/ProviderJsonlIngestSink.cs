using RemoteAgents.Agents;
using RemoteAgents.Events;

namespace RemoteAgents.Providers.Claude;

// T0 from the logging plan: on each agent Completed event, copy the
// provider's own session JSONL (Claude or Codex) into the orchestrator
// session dir so a single folder has the full tool-call / token-usage
// timeline alongside our transcript.
//
// Source locations:
//   ~/.claude/projects/<encoded-cwd>/<sessionId>.jsonl
//   ~/.codex/sessions/YYYY/MM/DD/rollout-*-<sessionId>.jsonl
//
// Encoded cwd: backslash, forward slash, and colon → dash. Example:
//   C:\Unity\CardFramework  ->  C--Unity-CardFramework
public sealed class ProviderJsonlIngestSink : IEventSink
{
    private readonly string _sessionDir;
    private readonly string _projectDir;
    private readonly Dictionary<string, int> _turnCounts = new();
    private readonly object _lock = new();

    public ProviderJsonlIngestSink(string sessionDir, string projectDir)
    {
        _sessionDir = sessionDir;
        _projectDir = projectDir;
    }

    public Task EmitAsync(AgentEvent evt, CancellationToken ct = default)
    {
        if (evt is AgentEvent.Completed c) Ingest(c);
        return Task.CompletedTask;
    }

    private void Ingest(AgentEvent.Completed c)
    {
        if (string.IsNullOrEmpty(c.SessionId)) return;

        var claudePath = TryFindClaudeJsonl(c.SessionId);
        if (claudePath is not null) { CopyTo("claude", claudePath); return; }

        var codexPath = TryFindCodexJsonl(c.SessionId);
        if (codexPath is not null) { CopyTo("codex", codexPath); return; }
    }

    private void CopyTo(string kind, string sourcePath)
    {
        int turn;
        lock (_lock)
        {
            _turnCounts.TryGetValue(kind, out turn);
            turn++;
            _turnCounts[kind] = turn;
        }
        var dest = Path.Combine(_sessionDir, $"{kind}-turn-{turn}.jsonl");
        File.Copy(sourcePath, dest, overwrite: true);
    }

    private string? TryFindClaudeJsonl(string sessionId)
    {
        var encoded = EncodeCwd(_projectDir);
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "projects", encoded, sessionId + ".jsonl");
        return File.Exists(path) ? path : null;
    }

    private static string? TryFindCodexJsonl(string sessionId)
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex", "sessions");
        if (!Directory.Exists(root)) return null;
        // rollout-*-<sessionId>.jsonl under any YYYY/MM/DD subtree
        return Directory.EnumerateFiles(root, $"rollout-*-{sessionId}.jsonl", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    public static string EncodeCwd(string path)
    {
        // Match Claude Code's on-disk encoding for its `projects/` folder names.
        return path
            .Replace('\\', '-')
            .Replace('/', '-')
            .Replace(':', '-');
    }
}
