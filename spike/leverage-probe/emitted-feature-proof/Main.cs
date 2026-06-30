using Acme.Users;
using Probe.Catalog;
using Probe.Runtime;

// Drives the EMITTED feature for real — the downstream consumer that "owns" the
// emitted code. Same behaviour the integration slice proved (add an item to a
// cart, wiring load-bearing), but the handler here was emitted from the terse
// MOTIF recipe: load/save/return + Repo<User> wiring were all motif-implied.

var repo = new Repo<User>().Seed("ada@example.com", new User("ada@example.com"));
var container = new Container()
    .Register(() => repo)
    .RegisterQuery<BookDetails>(_ => new BookDetails(49.99m, "Domain-Driven Design", "Eric Evans"));

// The generated wiring: fails fast if any discovered dep is unregistered.
AddItemHandler.RegisterDiscovered(container);
Console.WriteLine("RegisterDiscovered: all statically-discovered deps are bound — OK");

var bookId = Guid.Parse("11111111-1111-1111-1111-111111111111");
var command = new AddItemCommand("ada@example.com", bookId, 2);

var user = AddItemHandler.Handle(container.NewScope(), command);

Console.WriteLine($"AddItemToCart -> user {user.Email} now has {user.Cart.Count} cart item(s)");
var item = (CartItem)user.Cart[0];
Console.WriteLine($"  item: {item.Qty} x {item.Label} @ {item.Price} (BookId {item.BookId})");

// A second add proves it is a real mutation, not a one-shot.
AddItemHandler.Handle(container.NewScope(), new AddItemCommand("ada@example.com", bookId, 1));
Console.WriteLine($"after a second add -> cart has {user.Cart.Count} item(s)");

// The generated wiring is LOAD-BEARING (probe B): drop the query registration and
// RegisterDiscovered throws an actionable wiring-gap error.
var gap = new Container().Register(() => repo); // BookDetails query intentionally missing
try
{
    AddItemHandler.RegisterDiscovered(gap);
    Console.WriteLine("ERROR: expected a wiring-gap throw, got none");
}
catch (InvalidOperationException ex)
{
    Console.WriteLine($"wiring is load-bearing -> {ex.Message}");
}
