namespace Probe.Catalog;

// The hand-authored CATALOG the recipe composes against — written ONCE, reused by
// every recipe. Identical in spirit to the integration slice's catalog (this is
// reused infra, not author surface). Minimal RiverBooks-flavoured domain: the User
// aggregate, a generic repository, a query result. Nothing here is generated; the
// emitted handler resolves against it via scope.Get<T>() / scope.Ask<T>().

public sealed record User(string Email)
{
    public List<object> Cart { get; } = new();

    public void AddToCart(object item) => Cart.Add(item);
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
