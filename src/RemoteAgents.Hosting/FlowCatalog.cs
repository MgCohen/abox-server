using RemoteAgents.Flows;

namespace RemoteAgents.Hosting;

/// <summary>A registered flow: name → Type (resolved from DI per run) + description.</summary>
public sealed record FlowEntry(string Name, string Description, Type FlowType);

/// <summary>Declared once at the composition root via <c>AddFlow&lt;T&gt;</c>; injected into the catalog.</summary>
public sealed record FlowRegistration(string Name, string Description, Type FlowType);

/// <summary>
/// Name → flow-type registry. <c>POST /flows</c> resolves a name to a Type and asks
/// DI for it (R-SPINE-2 — no <c>new</c> in a catalog lambda). Distinct from
/// <see cref="FlowRegistry"/> (runtime, Guid-keyed live + history).
/// </summary>
public sealed class FlowCatalog
{
    private readonly Dictionary<string, FlowEntry> _byName = new(StringComparer.OrdinalIgnoreCase);

    public FlowCatalog(IEnumerable<FlowRegistration> registrations)
    {
        foreach (var r in registrations)
            _byName[r.Name] = new FlowEntry(r.Name, r.Description, r.FlowType);
    }

    public FlowEntry? Resolve(string name) => _byName.TryGetValue(name, out var e) ? e : null;

    public IReadOnlyList<FlowEntry> All() => [.. _byName.Values];
}
