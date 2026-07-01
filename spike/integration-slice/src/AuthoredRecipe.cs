namespace Slice;

// THE AUTHORED RECIPE (provisional API) — the one real vertical slice.
//
// Feature: AddItemToCart, modelled on RiverBooks. Load a User aggregate, add a
// line item, persist. It exercises all five mechanics in one flow:
//
//   1. MINT  (probe A) : CartItem is minted from the recipe's Models — a record
//                        the author names as data, lowered to the model file.
//   2. RENDER(probe D) : every TypeRef (return type User, dep Repo<User>,
//                        BookDetails) renders idiomatically with derived usings.
//   3. WIRE  (probe B) : the glue's scope.Get<Repo<User>>() / scope.Ask<BookDetails>()
//                        markers drive the emitted RegisterDiscovered.
//   4. GLUE  (pos. 4)  : the Body below — how the cart item is built and added —
//                        is the author's irreducible business logic, inline.
//   5. EMIT  (probe C) : `emit` lowers this to owned .cs at the configured target;
//      + FWD  (probe E) : the glue names the minted `CartItem` as a <T>-level type,
//                        resolved because mint feeds the shared resolution model.
static class AuthoredRecipe
{
    public static FeatureRecipe AddItemToCart() => new(
        Namespace: "Acme.Cart",
        FeatureName: "AddItemToCart",
        Command: "AddItemCommand",
        Models:
        [
            // probe A: minted from the use-site, as data.
            new ModelSpec("CartItem",
            [
                new FieldSpec("BookId", "Guid"),
                new FieldSpec("Qty", "int"),
                new FieldSpec("Price", "decimal"),
                new FieldSpec("Label", "string"),
            ]),
            // The command is itself a minted model — the trigger type.
            new ModelSpec("AddItemCommand",
            [
                new FieldSpec("Email", "string"),
                new FieldSpec("BookId", "Guid"),
                new FieldSpec("Qty", "int"),
            ]),
        ],
        Glue: new GlueSpec(
            // probe D: the return type is the catalog's User aggregate.
            Returns: "User",
            // position 4 — the irreducible business logic, authored inline. The
            // scope.Get/Ask markers (probe B) and the minted CartItem (<T>, probe E)
            // both appear here, in code the author actually writes.
            Body: """
                var users = scope.Get<Repo<User>>();
                var book = scope.Ask<BookDetails>(new BookDetailsQuery(command.BookId));
                var user = users.Load(command.Email);
                var item = new CartItem(command.BookId, command.Qty, book.Price, book.Label);
                user.AddToCart(item);
                users.Save();
                return user;
                """));
}
