namespace Probe.Runtime;

// The runtime DI seam the emitted feature runs against — written ONCE, reused
// (the hand-authored half of probe B's additive wiring). `scope.Get<T>()` /
// `scope.Ask<T>()` are REAL container-backed calls (so the glue compiles + runs as
// written) AND the exact call sites the wiring pass scans to emit
// RegisterDiscovered. No interceptors, no body rewrite.
//
// Committed with the emitted feature so the proof builds + runs.

public sealed class Container
{
    readonly Dictionary<Type, Func<object>> _services = new();
    readonly Dictionary<Type, Func<object, object>> _queries = new();

    public Container Register<T>(Func<T> factory) where T : notnull
    {
        _services[typeof(T)] = () => factory();
        return this;
    }

    public Container RegisterQuery<TResult>(Func<object, TResult> handler) where TResult : notnull
    {
        _queries[typeof(TResult)] = q => handler(q);
        return this;
    }

    public T Resolve<T>() where T : notnull =>
        _services.TryGetValue(typeof(T), out var f)
            ? (T)f()
            : throw new InvalidOperationException(
                $"No service for {typeof(T)}. Register it before a handler does scope.Get<{typeof(T).Name}>().");

    public TResult Run<TResult>(object query) where TResult : notnull =>
        _queries.TryGetValue(typeof(TResult), out var h)
            ? (TResult)h(query)
            : throw new InvalidOperationException(
                $"No query handler produces {typeof(TResult)}. Register it before scope.Ask<{typeof(TResult).Name}>().");

    bool HasService(Type t) => _services.ContainsKey(t);
    bool HasQuery(Type t) => _queries.ContainsKey(t);

    public Container EnsureService<T>() where T : notnull
    {
        if (!HasService(typeof(T)))
            throw new InvalidOperationException(
                $"Wiring gap: a handler needs service {typeof(T)} (scope.Get<{typeof(T).Name}>) but nothing registered it.");
        return this;
    }

    public Container EnsureQuery<T>() where T : notnull
    {
        if (!HasQuery(typeof(T)))
            throw new InvalidOperationException(
                $"Wiring gap: a handler asks for {typeof(T)} (scope.Ask<{typeof(T).Name}>) but no query produces it.");
        return this;
    }

    public Scope NewScope() => new(this);
}

public sealed class Scope(Container container)
{
    public T Get<T>() where T : notnull => container.Resolve<T>();
    public TResult Ask<TResult>(object query) where TResult : notnull => container.Run<TResult>(query);
}
