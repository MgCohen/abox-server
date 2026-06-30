namespace Probe.Domain;

// The world the recipe is TYPE-CHECKED against and the emitted feature RUNS against
// — written ONCE, reused (the catalog + runtime seam, reused infra from the earlier
// probes). The point of this probe: because these are real types, the author's glue
// lambda is checked against them by the compiler. `agg.AddToCart(...)` only compiles
// if User really has that method taking a CartItem; scope.Ask<BookDetails>(...) only
// compiles if the query result is really BookDetails. Drift a name or a type and the
// recipe does not build. No free text, nothing to drift.

public sealed record User(string Email)
{
    public List<CartItem> Cart { get; } = new();

    public void AddToCart(CartItem item) => Cart.Add(item);
}

public sealed class Repo<T> where T : notnull
{
    readonly Dictionary<string, T> _store = new();
    public Repo<T> Seed(string key, T value) { _store[key] = value; return this; }
    public T Load(string key) => _store.TryGetValue(key, out var v)
        ? v
        : throw new InvalidOperationException($"No {typeof(T).Name} for key '{key}'.");
    public void Save() { /* unit-of-work commit elided for the probe */ }
}

public sealed record BookDetails(decimal Price, string Title, string Author)
{
    public string Label => $"{Title} by {Author}";
}

public sealed record BookDetailsQuery(Guid BookId);

// The DI seam the emitted feature runs against (reused from probe B). scope.Get<T>() /
// scope.Ask<T>() are REAL container-backed calls AND the call sites the wiring pass
// scans to emit RegisterDiscovered. No interceptors, no body rewrite.

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
