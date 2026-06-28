namespace Spike;

// A catalog recipe: a named, parameterized unit that produces a declaration-tier artifact. This is
// the contract the (out-of-scope) task->recipe matcher binds to — name + description today, an
// explicit param schema only when the matcher needs to introspect it. The constructor IS the param
// surface for now (YAGNI): a recipe's inputs are its ctor parameters.
interface IRecipe
{
    string Name { get; }

    string Description { get; }

    TypeDecl Build();
}
