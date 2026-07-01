using Acme.Cart;
using Slice.Catalog;
using Slice.Runtime;

// Drives the EMITTED feature for real. This is the downstream consumer the
// emitted code is "owned" by: it does normal DI registration, calls the
// GENERATED RegisterDiscovered to pre-bind/validate the discovered deps (probe B),
// then dispatches the handler — whose body is the authored glue (position 4) over
// the minted CartItem model (probe A/E).

// A single seeded repo instance so the aggregate mutation persists across calls
// (the factory is a singleton-by-capture; transient would give a fresh User each
// resolve — a runtime DI choice, not a composition concern).
var repo = new Repo<User>().Seed("ada@example.com", new User("ada@example.com"));
var container = new Container()
    .Register(() => repo)
    .RegisterQuery<BookDetails>(_ => new BookDetails(49.99m, "Domain-Driven Design", "Eric Evans"));

// The generated wiring: fails fast if any discovered dep is unregistered.
AddItemToCartHandler.RegisterDiscovered(container);
Console.WriteLine("RegisterDiscovered: all statically-discovered deps are bound — OK");

var bookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
var command = new AddItemCommand("ada@example.com", bookId, 2);

var user = AddItemToCartHandler.Handle(container.NewScope(), command);

Console.WriteLine($"AddItemToCart -> user {user.Email} now has {user.Cart.Count} cart item(s)");
var item = (CartItem)user.Cart[0];
Console.WriteLine($"  item: {item.Qty} x {item.Label} @ {item.Price} (BookId {item.BookId})");

// A second add proves it is a real mutation, not a one-shot.
AddItemToCartHandler.Handle(container.NewScope(), new AddItemCommand("ada@example.com", bookId, 1));
Console.WriteLine($"after a second add -> cart has {user.Cart.Count} item(s)");

// The generated wiring is LOAD-BEARING (probe B): drop the query registration and
// RegisterDiscovered throws an actionable wiring-gap error — proof the static
// manifest and the runtime container are cross-checked, not decorative.
var gap = new Container().Register(() => repo); // BookDetails query intentionally missing
try
{
    AddItemToCartHandler.RegisterDiscovered(gap);
    Console.WriteLine("ERROR: expected a wiring-gap throw, got none");
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"wiring is load-bearing -> {ex.Message}");
}
