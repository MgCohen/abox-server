using Probe.Domain;

namespace Probe.Shapes;

// ============================================================================
// THE DESIGN SPACE — "how many ways can we turn the feature into a composed object?"
//
// Three author surfaces, ALL type-checked against the same domain, ALL expressing
// the same feature with the same real-code glue. They differ only in ergonomics —
// the scaffold extraction and the typed-glue principle are identical. Shape B (the
// fluent builder, in AuthoredRecipe.cs) is the one the emitter actually lowers; A
// and C are here compile-only to show the principle is shape-independent. The lift
// (Lift.cs) works the same on a lambda argument or an override method body.
//
// The common thread: the variant TYPES are generic arguments and the business logic
// is a real lambda/method. No shape reintroduces a string.
// ============================================================================

// --- Shape A — a plain record of typed delegates (no builder) ---------------
static class ShapeA_RecordOfDelegates
{
    public static Mutation<User, AddItemCommand> AddItemToCart() => new(
        loadBy: cmd => cmd.Email,
        with: (cart, cmd, ctx) =>
        {
            var book = ctx.Ask<BookDetails>(new BookDetailsQuery(cmd.BookId));
            cart.AddToCart(new CartItem(cmd.BookId, cmd.Qty, book.Price, book.Label));
        });
}

// --- Shape C — generic base + override (the "wrap the endpoint" form) --------
// Closest to the endpoint example: the base hardcodes the invariants (the scaffold);
// the leaf overrides only the typed variant slots. An emitter lifts the override
// bodies exactly as it lifts the builder's lambdas.
abstract class MutationFeature<TAgg, TCmd>
    where TAgg : notnull
    where TCmd : notnull
{
    protected abstract string LoadBy(TCmd command);
    protected abstract void With(TAgg agg, TCmd command, Scope scope);
}

sealed class AddItemFeature : MutationFeature<User, AddItemCommand>
{
    protected override string LoadBy(AddItemCommand command) => command.Email;

    protected override void With(User agg, AddItemCommand command, Scope scope)
    {
        var book = scope.Ask<BookDetails>(new BookDetailsQuery(command.BookId));
        agg.AddToCart(new CartItem(command.BookId, command.Qty, book.Price, book.Label));
    }
}
