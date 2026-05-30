using System.Collections.Concurrent;
using RemoteAgents.Flows;

namespace RemoteAgents.Hosting;

// In-memory name → IFlow catalog. The CLI dispatcher and the Host's
// in-process executor both resolve flow definitions by name through this
// single source of truth.
//
// Note: this is distinct from the runtime FlowRegistry introduced in
// Workstream B which maps Guid run-ids → live Flow aggregates. Different
// concept, different name.
public sealed class FlowCatalog
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
