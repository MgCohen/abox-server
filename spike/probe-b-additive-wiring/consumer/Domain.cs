namespace ProbeB;

// Minimal RiverBooks-flavoured domain so the authored handlers below have real
// types to resolve and wire. Nothing here is generated — it's the catalog the
// agent composes against.

public sealed record User(string Email)
{
    public List<CartItem> Cart { get; } = new();
    public void AddItemToCart(CartItem item) => Cart.Add(item);
}

public sealed record CartItem(Guid BookId, int Qty, decimal Price, string Label);

public sealed record BookDetails(decimal Price, string Title, string Author)
{
    public string Label => $"{Title} by {Author}";
}

public sealed record BookDetailsQuery(Guid BookId);

// A generic repository capability the handler resolves via scope.Get<Repo<User>>().
public sealed class Repo<T>
{
    readonly Dictionary<string, T> _store = new();
    public Repo<T> Seed(string key, T value) { _store[key] = value; return this; }
    public T Get(string key) => _store.TryGetValue(key, out var v)
        ? v
        : throw new InvalidOperationException($"No {typeof(T).Name} for key '{key}'.");
    public void Save() { /* unit-of-work commit elided for the spike */ }
}

// Commands (the trigger types the wrappers key on).
public sealed record AddItem(string Email, Guid BookId, int Qty);
public sealed record TopSalesByMonth(int Month, int Year);
public sealed record Reindex;

// Projection read result.
public sealed record BookSalesResult(Guid BookId, string Title, decimal Sales);

// A read-store capability resolved via scope.Get<Read>().
public sealed class Read
{
    public IReadOnlyList<BookSalesResult> TopBooks(int month, int year) =>
    [
        new BookSalesResult(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Domain-Driven Design", 4200m),
        new BookSalesResult(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Refactoring", 3100m),
    ];
}
