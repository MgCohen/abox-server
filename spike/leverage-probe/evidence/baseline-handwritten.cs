// BASELINE — the plain feature a developer would hand-write, with NO recipe/motif
// machinery at all. The "real case" the recipe must beat. This is what you'd type
// straight into the codebase: the minted models as records + the handler method,
// in the same catalog/runtime terms the emitted feature uses.
//
// Counted surface = everything below the `=== BASELINE ===` markers (the records +
// the handler). The using lines and namespace are counted too (a dev writes them).
//
// === BASELINE (begin) ===
using Probe.Catalog;
using Probe.Runtime;

namespace Acme.Users;

public sealed record AddItemCommand(string Email, Guid BookId, int Qty);
public sealed record CartItem(Guid BookId, int Qty, decimal Price, string Label);

public static class AddItemHandler
{
    public static User Handle(Scope scope, AddItemCommand command)
    {
        var users = scope.Get<Repo<User>>();
        var user = users.Load(command.Email);
        var book = scope.Ask<BookDetails>(new BookDetailsQuery(command.BookId));
        user.AddToCart(new CartItem(command.BookId, command.Qty, book.Price, book.Label));
        users.Save();
        return user;
    }
}
// === BASELINE (end) ===
//
// NOTE: the hand-written baseline does NOT get the generated RegisterDiscovered
// wiring-validation for free — a dev either omits it (losing the load-bearing
// wiring check) or hand-writes it, ADDING surface. Counted here at its cheapest:
// omitted. The motif recipe gets RegisterDiscovered emitted for free.
