using RemoteAgents.Flows;

namespace RemoteAgents.Hosting;

// Per-invocation Flow construction inputs. The runtime FlowRegistry's
// caller (POST /flows) builds one of these from the REST body, hands it
// to the factory, and gets back a fresh Flow aggregate.
public sealed record FlowSpec(string Project, string ProjectDir, string Prompt, string[] Args, bool ShouldPush);

// One entry in the catalog. Name + human-readable description (for the
// /catalog endpoint) + the factory that builds a fresh Flow for each
// invocation (per D5 — tools are injected per-flow, not shared singletons).
public sealed record FlowDef(string Name, string Description, Func<FlowSpec, Flow> Factory);

// Name → flow-definition registry. The Host's POST /flows resolves by
// name, the GET /catalog endpoint enumerates this.
//
// Distinct from FlowRegistry (runtime, Guid-keyed live + history).
public sealed class FlowCatalog
{
    private readonly Dictionary<string, FlowDef> _flows = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string name, string description, Func<FlowSpec, Flow> factory)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("flow name is required", nameof(name));
        _flows[name] = new FlowDef(name, description, factory);
    }

    public FlowDef? Get(string name) =>
        _flows.TryGetValue(name, out var def) ? def : null;

    public IReadOnlyCollection<FlowDef> All() => _flows.Values.ToArray();
}
