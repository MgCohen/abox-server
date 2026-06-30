using Probe.Catalog;

namespace Probe;

// ============================================================================
// THE AUTHORED MOTIF RECIPE — the measured author surface.
//
// Same feature as the integration slice (AddItemToCart): load a User, build a
// cart line-item, add it, persist. But under the `Mutates<User>` motif, the author
// writes ONLY the divergence — never the load/save/return/repo-wiring scaffold,
// never field-by-field model ceremony, never the FeatureRecipe/GlueSpec wrappers.
//
// Everything between `=== AUTHORED ===` markers is what the author wrote and what
// the surface-area comparison counts. Compare against integration-slice's
// AuthoredRecipe.cs (the verbose version) and the hand-written baseline.
// ============================================================================
static class AuthoredRecipe
{
    // === AUTHORED (begin) ===
    public static MutationRecipe AddItemToCart() => Feature.Mutates<User>(
        key: "command.Email",
        command: "AddItemCommand(Email:string, BookId:Guid, Qty:int)",
        models: "CartItem(BookId:Guid, Qty:int, Price:decimal, Label:string)",
        with: """
            var book = scope.Ask<BookDetails>(new BookDetailsQuery(command.BookId));
            agg.AddToCart(new CartItem(command.BookId, command.Qty, book.Price, book.Label));
            """);
    // === AUTHORED (end) ===
}
