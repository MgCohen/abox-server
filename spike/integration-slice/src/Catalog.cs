namespace Slice.Catalog;

// The hand-authored CATALOG the recipe composes against (position 1/2: the
// components that already exist). Minimal RiverBooks-flavoured domain — the
// User aggregate, a generic repository, a query result. Nothing here is
// generated; it is the reusable surface the emitted handler resolves via
// scope.Get<T>() / scope.Ask<T>().
//
// This source is also fed to the slice's emit tool as the "catalog support
// source" so the semantic renderer (probe D) and the wiring scan (probe B) can
// resolve Repo<User>, BookDetails, etc. to real symbols, and so the minted
// CartItem model resolves alongside them (probe E).

public sealed record User(string Email)
{
    public List<object> Cart { get; } = new();

    // The aggregate's behaviour. The glue calls this; the cart item it receives
    // is a MINTED model, passed as object so the catalog need not reference it
    // (the minted type lives in the emitted feature namespace). A real catalog
    // would take a typed line-item; object keeps the catalog minting-agnostic.
    public void AddToCart(object item) => Cart.Add(item);
}

public sealed class Repo<T> where T : notnull
{
    readonly Dictionary<string, T> _store = new();
    public Repo<T> Seed(string key, T value) { _store[key] = value; return this; }
    public T Load(string key) => _store.TryGetValue(key, out var v)
        ? v
        : throw new InvalidOperationException($"No {typeof(T).Name} for key '{key}'.");
    public void Save() { /* unit-of-work commit elided for the slice */ }
}

public sealed record BookDetails(decimal Price, string Title, string Author)
{
    public string Label => $"{Title} by {Author}";
}

public sealed record BookDetailsQuery(Guid BookId);
