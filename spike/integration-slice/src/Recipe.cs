namespace Slice;

// ============================================================================
// PROVISIONAL / THROWAWAY recipe-authoring API.
//
// This is the smallest dialect needed to drive the integration slice end-to-end.
// It is NOT the chosen design — the authoring surface is explicitly undecided
// (README → "Provisional API caveat"). The point is to prove the *tech path*:
// that mint + render + wire + glue + emit compose into one feature emission.
// Treat every type name here as placeholder.
//
// A FeatureRecipe is one aggregate-mutation slice (modelled on RiverBooks
// AddItemToCart): mint some models, load an aggregate, run a glue lambda over
// it, persist. It carries everything the four emit passes need:
//
//   Models  -> probe A : minted records, lowered to a model file
//   Glue    -> position 4 : the irreducible business logic, authored inline as
//              C# text the author writes (how the cart item is found/added)
//   the glue text also carries scope.Get<T>() markers -> probe B wiring scan
//   TypeRef everywhere -> probe D semantic rendering (idiomatic + derived usings)
//   minted model names referenced as <T> across files -> probe E forward-ref
// ============================================================================

sealed record FeatureRecipe(
    string Namespace,
    string FeatureName,
    string Command,
    IReadOnlyList<ModelSpec> Models,
    GlueSpec Glue);

// A model the recipe mints at its use-site (probe A's CreateModel, as data).
// Lowered to an owned record in the model file; usable as <T> elsewhere (probe E).
sealed record ModelSpec(string Name, IReadOnlyList<FieldSpec> Fields);

sealed record FieldSpec(string Name, TypeRef Type);

// The glue (position 4): the feature's irreducible business logic, authored as
// the body of `(scope, command) => { ... }`. The author writes real C# here.
//   - It is dropped verbatim into the emitted handler (the typed glue slot).
//   - Its `scope.Get<T>()` calls are ALSO the markers the wiring pass scans
//     (probe B) to emit DI registration — additively, no rewrite of the body.
// `Returns` is the handler's declared return type (a TypeRef so it renders
// through the semantic model and can name a minted model).
sealed record GlueSpec(TypeRef Returns, string Body);

// A reference to a type, authored as the C# type text the author would write
// inline: "int", "string", "Guid", "Repo<User>", a minted model name "CartItem".
// The renderer resolves this to a real ITypeSymbol (probe D); a minted name
// resolves because the slice compiles the minted models into the same
// resolution compilation first (probe E, same-compilation forward ref).
sealed record TypeRef(string Text)
{
    public static implicit operator TypeRef(string text) => new(text);
}
