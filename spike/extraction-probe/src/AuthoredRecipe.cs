using Probe.Domain;

namespace Probe;

// ============================================================================
// THE AUTHORED RECIPE — the measured author surface (Shape B: fluent builder).
//
// Same feature as the leverage probe (AddItemToCart): load a User, build a cart
// line-item, add it, persist. But the rejected version expressed the glue as free
// text strings (key: "command.Email", with: """...verbatim handler copy..."""). Here
// EVERY part is typed:
//
//   * Mutates<User, AddItemCommand>()  -- the variant types are GENERIC ARGUMENTS,
//     so the aggregate, its Repo<User>, the return type, and the command are fixed
//     by the type system, not spelled in a string.
//
//   * LoadBy(cmd => cmd.Email)         -- REAL CODE. `cmd.Email` only compiles
//     because AddItemCommand really has an Email. Rename the field and this fails to
//     build.
//
//   * With((cart, cmd, ctx) => { ... }) -- REAL CODE. Every line is checked: ctx.Ask
//     returns BookDetails; cart.AddToCart takes a CartItem; the CartItem ctor args
//     match its (minted) fields. Drift any of them and the recipe does not compile.
//
// The author chose natural parameter names (cart/cmd/ctx); the emitter renames them
// to the canonical handler names while lifting (Lift.cs) — proving the lift is a real
// syntactic transform, not a copy-paste of a string.
//
// CartItem / AddItemCommand are minted types (Minted.cs) — the closed source-gen
// path, out of scope here. Everything between the AUTHORED markers is what the
// surface-area comparison counts.
// ============================================================================
static class AuthoredRecipe
{
    // === AUTHORED (begin) ===
    public static Mutation<User, AddItemCommand> AddItemToCart() => Feature
        .Mutates<User, AddItemCommand>()
        .LoadBy(cmd => cmd.Email)
        .With((cart, cmd, ctx) =>
        {
            var book = ctx.Ask<BookDetails>(new BookDetailsQuery(cmd.BookId));
            cart.AddToCart(new CartItem(cmd.BookId, cmd.Qty, book.Price, book.Label));
        });
    // === AUTHORED (end) ===
}
