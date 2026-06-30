namespace ProbeB;

// The honest, fully-additive seam the reviewer asked for: a REAL runtime container
// plus a `scope` object whose Get<T>() / Ask<T>() actually resolve. These same call
// sites are ALSO the markers the generator scans — no interceptors, no body rewrite.

public sealed class Container
{
    readonly Dictionary<Type, Func<object>> _services = new();
    readonly Dictionary<Type, Func<object, object>> _queryHandlers = new();

    public Container Register<T>(Func<T> factory) where T : notnull
    {
        _services[typeof(T)] = () => factory();
        return this;
    }

    // A query handler keyed by the RESULT type the query produces.
    public Container RegisterQuery<TResult>(Func<object, TResult> handler) where TResult : notnull
    {
        _queryHandlers[typeof(TResult)] = q => handler(q);
        return this;
    }

    public T Resolve<T>() where T : notnull
    {
        if (_services.TryGetValue(typeof(T), out var f))
            return (T)f();
        throw new InvalidOperationException(
            $"No service registered for {typeof(T)}. Register it before composing a handler that does scope.Get<{typeof(T).Name}>().");
    }

    public TResult Run<TResult>(object query) where TResult : notnull
    {
        if (_queryHandlers.TryGetValue(typeof(TResult), out var h))
            return (TResult)h(query);
        throw new InvalidOperationException(
            $"No query handler registered producing {typeof(TResult)}. Register it before composing a handler that does scope.Ask<{typeof(TResult).Name}>().");
    }

    public bool HasService(Type t) => _services.ContainsKey(t);
    public bool HasQuery(Type resultType) => _queryHandlers.ContainsKey(resultType);

    // Called by the GENERATED RegisterDiscovered: fail fast if a statically-found
    // dependency has no registration. The generator enumerates which T's to check;
    // these methods are the hand-authored half of the additive seam.
    public Container EnsureService<T>() where T : notnull
    {
        if (!HasService(typeof(T)))
            throw new InvalidOperationException(
                $"Wiring gap: a handler needs service {typeof(T)} (scope.Get<{typeof(T).Name}>) but the container has no registration.");
        return this;
    }

    public Container EnsureQuery<T>() where T : notnull
    {
        if (!HasQuery(typeof(T)))
            throw new InvalidOperationException(
                $"Wiring gap: a handler asks for {typeof(T)} (scope.Ask<{typeof(T).Name}>) but no query handler produces it.");
        return this;
    }

    public Scope NewScope() => new(this);
}

// `scope` in the authored lambda. Get<T> / Ask<T> are real, container-backed calls.
public sealed class Scope(Container container)
{
    // Capability marker AND real call: resolve a service from the container.
    public T Get<T>() where T : notnull => container.Resolve<T>();

    // Capability marker AND real call: dispatch a query, get its typed result.
    public TResult Ask<TResult>(object query) where TResult : notnull => container.Run<TResult>(query);
}
