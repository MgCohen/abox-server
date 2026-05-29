using System.Collections.Concurrent;
using RemoteAgents.Flows;

namespace RemoteAgents.Hosting;

// In-memory IFlow registry. The CLI dispatcher and the Host's in-process
// executor both resolve by name through this single source of truth —
// replaces the duplicate file-system globs in cli/agents-dotnet.cs and
// Host/Program.cs.
public sealed class FlowRegistry
{
    private readonly ConcurrentDictionary<string, IFlow> _flows = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IFlow flow)
    {
        if (string.IsNullOrEmpty(flow.Name))
            throw new ArgumentException("flow.Name is required", nameof(flow));
        _flows[flow.Name] = flow;
    }

    public IFlow? Get(string name) =>
        _flows.TryGetValue(name, out var f) ? f : null;

    public IReadOnlyCollection<IFlow> All() => _flows.Values.ToArray();
}
