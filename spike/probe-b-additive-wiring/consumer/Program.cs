using ProbeB;

// ── The AUTHORED use-site (position 2, D2 scope form) ────────────────────────
// This is exactly what the agent composes. scope.Get<T>() / scope.Ask<T>() are
// real, container-backed calls — so this block COMPILES and RUNS as written. The
// generator never edits it; it only SCANS these same call sites for the markers.

// Pattern A — aggregate mutation.
App.Handler<AddItem>(scope =>
{
    var users = scope.Get<Repo<User>>();
    var book = scope.Ask<BookDetails>(new BookDetailsQuery(default));
    var user = users.Get("ada@example.com");
    user.AddItemToCart(new CartItem(default, 1, book.Price, book.Label));
    users.Save();
    return user;
});

// Pattern B — projection read.
App.Endpoint<TopSalesByMonth>(scope =>
{
    var db = scope.Get<Read>();
    return db.TopBooks(6, 2026);
});

// Looped use-site: scope.Get<Read>() is called many times at runtime, yet the
// generator records the dependency ONCE — the manifest is "distinct types this
// handler references", not a call count. (Limitation demo: see README.)
App.Handler<Reindex>(scope =>
{
    var rows = 0;
    for (var page = 0; page < 3; page++)
    {
        var db = scope.Get<Read>();
        rows += db.TopBooks(page, 2026).Count;
    }
    return rows;
});

// ── Runtime wiring (normal DI; nothing generated yet) ────────────────────────
App.Container
    .Register(() => new Repo<User>().Seed("ada@example.com", new User("ada@example.com")))
    .Register(() => new Read())
    .RegisterQuery<BookDetails>(_ => new BookDetails(49.99m, "Domain-Driven Design", "Eric Evans"));

// ── The GENERATED additive artifact, used for real ───────────────────────────
// Wiring.Manifest + Wiring.RegisterDiscovered were emitted by the source generator
// from the markers above. We print the manifest and run the pre-bind validation.
Console.WriteLine("=== Generated manifest (statically discovered from scope markers) ===");
foreach (var h in Wiring.Manifest)
{
    Console.WriteLine($"handler {h.Command}");
    foreach (var d in h.Dependencies)
        Console.WriteLine($"    {d.Kind,-8} {d.Type}");
}

Console.WriteLine();
Console.WriteLine("=== RegisterDiscovered: pre-bind/validate every discovered dep ===");
Wiring.RegisterDiscovered(App.Container);
Console.WriteLine("all discovered dependencies are registered in the container — OK");

Console.WriteLine();
Console.WriteLine("=== Dispatch the handlers (the authored bodies actually run) ===");
var added = (User)App.Dispatch(new AddItem("ada@example.com", default, 1));
Console.WriteLine($"AddItem -> user {added.Email} now has {added.Cart.Count} cart item(s): {added.Cart[0].Label} @ {added.Cart[0].Price}");

var report = (IReadOnlyList<BookSalesResult>)App.Dispatch(new TopSalesByMonth(6, 2026));
Console.WriteLine($"TopSalesByMonth -> {report.Count} rows; top = {report[0].Title} ({report[0].Sales})");

var reindexed = (int)App.Dispatch(new Reindex());
Console.WriteLine($"Reindex -> visited {reindexed} rows over 3 pages (scope.Get<Read> called thrice, discovered once)");
