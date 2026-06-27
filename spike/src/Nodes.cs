namespace Spike;

// The normalized contract (the Func/Action analogue):
//   IExpr<T> — produces a value of type T
//   IStmt    — produces statement(s)
// Recipe fills are typed against these, so composition is checked at authoring time:
// an IExpr<int> param rejects anything that isn't an int producer.
interface IStmt;

interface IExpr<out T>;

// A variable handle: identity + type, created once as a local and threaded into the nodes
// that declare or reference it. Replaces stringly-typed names, so refs are typed (Ref(acc)
// is IExpr<int>) and rename-safe. The name is chosen once, here, never re-spelled at a use.
interface IVar
{
    string Name { get; }
}

sealed record Var<T>(string Name) : IVar;

// Atoms — too trivial to be snippets; rendered directly by the generator.
sealed record Lit(int Value) : IExpr<int>;

sealed record Ref(Var<int> Var) : IExpr<int>;

// A sequence of statements. Used as the recipe root and as a block-region fill.
sealed record Block(params IStmt[] Statements)
{
    // Snippet-side marker: "a block region goes here", identified by id. The generator
    // replaces the call with the rendered block; it is never executed.
    public static Block Of(string id) =>
        throw new InvalidOperationException($"Block.Of(\"{id}\") is a compile-time placeholder; never executed.");
}

// The snippet-backed recipe nodes (DefineNode, AddNode, LoopNode, …) are SOURCE-GENERATED
// from the [Snippet] methods into Nodes.Generated.cs. Regenerate with:
//   dotnet run --project spike/gen
