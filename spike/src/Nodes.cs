namespace Spike;

// The normalized contract (the Func/Action analogue):
//   IExpr<T> — produces a value of type T
//   IStmt    — produces statement(s)
// Recipe holes are typed against these, so composition is checked at authoring time:
// an IExpr<int> hole rejects anything that isn't an int producer.
interface IStmt;

interface IExpr<out T>;

// Atoms — too trivial to be snippets; rendered directly by the generator.
sealed record Lit(int Value) : IExpr<int>;

sealed record Ref(string Name) : IExpr<int>;

// A sequence of statements. Doubles as the recipe root and as a body-hole filler.
sealed record Block(params IStmt[] Statements);

// The snippet-backed recipe nodes (DefineNode, AddNode, LoopNode, …) are SOURCE-GENERATED
// from the [Snippet] methods into Nodes.Generated.cs. Regenerate with:
//   dotnet run --project spike/gen
